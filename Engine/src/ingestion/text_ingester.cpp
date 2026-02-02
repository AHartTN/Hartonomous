/**
 * @file text_ingester.cpp
 * @brief Universal text ingestion implementation with deterministic UTF-8 decoding
 */

#include <ingestion/text_ingester.hpp>
#include <storage/physicality_store.hpp>
#include <storage/atom_store.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/content_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <iostream>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <algorithm>
#include <cmath>

namespace Hartonomous {

TextIngester::TextIngester(PostgresConnection& db, const IngestionConfig& config)
    : db_(db), config_(config) {
    NGramConfig ngram_config;
    ngram_config.min_n = config_.min_ngram_size;
    ngram_config.max_n = config_.max_ngram_size;
    ngram_config.min_frequency = config_.min_frequency;
    ngram_config.cooccurrence_window = config_.cooccurrence_window;
    ngram_config.track_positions = true;
    ngram_config.track_direction = true;
    extractor_ = NGramExtractor(ngram_config);
}

std::string TextIngester::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

std::string TextIngester::hash_to_hex(const BLAKE3Pipeline::Hash& hash) {
    return BLAKE3Pipeline::to_hex(hash);
}

std::u32string TextIngester::utf8_to_utf32(const std::string& s) {
    std::u32string out;
    size_t i = 0;
    while (i < s.size()) {
        uint8_t c = s[i];
        char32_t cp = 0;
        size_t len = 0;
        if (c < 0x80) { cp = c; len = 1; }
        else if ((c >> 5) == 0x6) { cp = c & 0x1F; len = 2; }
        else if ((c >> 4) == 0xE) { cp = c & 0x0F; len = 3; }
        else if ((c >> 3) == 0x1E) { cp = c & 0x07; len = 4; }
        else throw std::runtime_error("Invalid UTF-8");
        for (size_t j = 1; j < len; ++j) {
            if (i + j >= s.size()) throw std::runtime_error("Truncated UTF-8");
            uint8_t cc = s[i + j];
            if ((cc >> 6) != 0x2) throw std::runtime_error("Invalid UTF-8 continuation");
            cp = (cp << 6) | (cc & 0x3F);
        }
        if (cp != 0) out.push_back(cp); // Skip null characters
        i += len;
    }
    return out;
}

std::string TextIngester::utf32_to_utf8(const std::u32string& s) {
    std::string out;
    for (char32_t cp : s) {
        if (cp == 0) continue; // Postgres doesn't allow U+0000 in TEXT fields
        if (cp < 0x80) out.push_back(static_cast<char>(cp));
        else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }
    return out;
}

Vec4 TextIngester::compute_centroid(const std::vector<Vec4>& positions) {
    if (positions.empty()) return Vec4(1.0, 0.0, 0.0, 0.0);
    Vec4 sum = Vec4::Zero();
    for (const auto& pos : positions) sum += pos;
    Vec4 avg = sum / static_cast<double>(positions.size());
    double norm = avg.norm();
    if (norm < 1e-10) return Vec4(1.0, 0.0, 0.0, 0.0);
    return avg / norm;
}

TextIngester::Physicality TextIngester::compute_physicality(const Vec4& centroid) {
    Physicality phys;
    phys.centroid = centroid;
    Eigen::Vector4d hypercube;
    for (int i = 0; i < 4; ++i) hypercube[i] = (centroid[i] + 1.0) / 2.0;
    phys.hilbert_index = HilbertCurve4D::encode(hypercube);
    std::vector<uint8_t> data;
    data.push_back(0x50); // Domain tag 'P' for Physicality
    data.insert(data.end(), reinterpret_cast<const uint8_t*>(centroid.data()), reinterpret_cast<const uint8_t*>(centroid.data()) + 4 * sizeof(double));
    phys.id = BLAKE3Pipeline::hash(data);
    return phys;
}

BLAKE3Pipeline::Hash TextIngester::compute_sequence_hash(const std::vector<SequenceItem>& sequence, uint8_t type_tag) {
    std::vector<uint8_t> data;
    data.push_back(type_tag);
    data.reserve(1 + sequence.size() * (BLAKE3Pipeline::HASH_SIZE + sizeof(uint32_t)));
    for (const auto& item : sequence) {
        data.insert(data.end(), item.id.begin(), item.id.end());
        uint32_t ord = item.ordinal;
        data.insert(data.end(), reinterpret_cast<uint8_t*>(&ord), reinterpret_cast<uint8_t*>(&ord) + sizeof(ord));
    }
    return BLAKE3Pipeline::hash(data);
}

BLAKE3Pipeline::Hash TextIngester::create_content_record(const std::string& text) {
    auto content_hash = BLAKE3Pipeline::hash(text);
    std::vector<uint8_t> id_data;
    id_data.push_back(0x43); // Type tag 'C' for Content
    id_data.insert(id_data.end(), content_hash.begin(), content_hash.end());
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    return BLAKE3Pipeline::hash(id_data);
}

std::vector<TextIngester::Atom> TextIngester::extract_atoms(const std::u32string& text) {
    std::vector<Atom> atoms;
    std::unordered_set<char32_t> seen_codepoints;
    for (char32_t cp : text) {
        if (seen_codepoints.count(cp)) continue;
        seen_codepoints.insert(cp);
        Atom atom;
        atom.codepoint = cp;
        auto proj = CodepointProjection::project(cp);
        atom.id = proj.hash;
        atom.physicality.centroid = proj.s3_position;
        atom.physicality.hilbert_index = proj.hilbert_index;
        atom.physicality = compute_physicality(proj.s3_position);
        atoms.push_back(atom);
    }
    return atoms;
}

std::vector<TextIngester::Composition> TextIngester::extract_compositions(
    [[maybe_unused]] const std::u32string& text,
    const std::unordered_map<BLAKE3Pipeline::Hash, Atom, HashHasher>& atom_map) {
    std::vector<Composition> compositions;
    auto significant = extractor_.significant_ngrams();
    for (const NGram* ngram : significant) {
        Composition comp;
        comp.text = ngram->text;
        comp.id = ngram->hash;
        std::vector<Vec4> atom_positions;
        uint32_t ordinal = 0;
        for (char32_t cp : ngram->text) {
            auto atom_hash = CodepointProjection::project(cp).hash;
            auto atom_it = atom_map.find(atom_hash);
            if (atom_it != atom_map.end()) {
                comp.sequence.push_back({atom_it->second.id, ordinal++, 1});
                atom_positions.push_back(atom_it->second.physicality.centroid);
            }
        }
        comp.physicality = compute_physicality(compute_centroid(atom_positions));
        compositions.push_back(comp);
    }
    return compositions;
}

std::vector<TextIngester::Relation> TextIngester::extract_relations(
    const std::unordered_map<BLAKE3Pipeline::Hash, Composition, HashHasher>& comp_map) {
    std::vector<Relation> relations;
    auto significant = extractor_.significant_cooccurrences(config_.min_cooccurrence);
    for (const CoOccurrence* cooc : significant) {
        auto it_a = comp_map.find(cooc->ngram_a);
        auto it_b = comp_map.find(cooc->ngram_b);
        if (it_a == comp_map.end() || it_b == comp_map.end()) continue;
        Relation rel;
        rel.sequence.push_back({cooc->ngram_a, cooc->is_forward() ? 0u : 1u, cooc->count});
        rel.sequence.push_back({cooc->ngram_b, cooc->is_forward() ? 1u : 0u, cooc->count});
        rel.id = compute_sequence_hash(rel.sequence, 0x02); // 0x02 for Relation
        rel.physicality = compute_physicality(compute_centroid({it_a->second.physicality.centroid, it_b->second.physicality.centroid}));
        rel.initial_elo = 800.0 + 700.0 * cooc->signal_strength();
        rel.is_forward = cooc->is_forward();
        relations.push_back(rel);
    }
    return relations;
}

void TextIngester::store_all(
    const BLAKE3Pipeline::Hash& content_id,
    const std::vector<Atom>& atoms,
    const std::vector<Composition>& compositions,
    const std::vector<Relation>& relations,
    IngestionStats& stats) {
    PostgresConnection::Transaction txn(db_);
    ContentStore(db_).store({content_id, config_.tenant_id, config_.user_id, config_.content_type, content_id, stats.original_bytes, config_.mime_type, config_.language, config_.source, config_.encoding});
    PhysicalityStore phys_store(db_);
    auto try_store_phys = [&](const BLAKE3Pipeline::Hash& id, const HilbertCurve4D::HilbertIndex& h, const Vec4& c) { if (seen_physicality_ids_.insert(id).second) phys_store.store({id, h, c}); };
    for (const auto& a : atoms) try_store_phys(a.physicality.id, a.physicality.hilbert_index, a.physicality.centroid);
    for (const auto& c : compositions) try_store_phys(c.physicality.id, c.physicality.hilbert_index, c.physicality.centroid);
    for (const auto& r : relations) try_store_phys(r.physicality.id, r.physicality.hilbert_index, r.physicality.centroid);
    phys_store.flush();
    
    // === 3. Store Atoms ===
    AtomStore atom_store(db_);
    for (const auto& a : atoms) {
        if (seen_atom_ids_.insert(a.id).second) {
            atom_store.store({a.id, a.physicality.id, a.codepoint});
            stats.atoms_new++;
        }
    }
    atom_store.flush();

    // === 4. Store Compositions ===
    CompositionStore comp_store(db_);
    for (const auto& c : compositions) {
        if (seen_composition_ids_.insert(c.id).second) {
            comp_store.store({c.id, c.physicality.id});
            stats.compositions_new++;
        }
    }
    comp_store.flush();

    CompositionSequenceStore comp_seq_store(db_);
    for (const auto& c : compositions) for (const auto& item : c.sequence) {
        std::vector<uint8_t> data; data.push_back(0x01); // CompositionSequence tag
        data.insert(data.end(), c.id.begin(), c.id.end()); data.insert(data.end(), item.id.begin(), item.id.end());
        uint32_t ord = item.ordinal; data.insert(data.end(), reinterpret_cast<uint8_t*>(&ord), reinterpret_cast<uint8_t*>(&ord) + sizeof(ord));
        comp_seq_store.store({BLAKE3Pipeline::hash(data), c.id, item.id, item.ordinal, item.occurrences});
    }
    comp_seq_store.flush();

    RelationStore rel_store(db_);
    for (const auto& r : relations) if (seen_relation_ids_.insert(r.id).second) { rel_store.store({r.id, r.physicality.id}); stats.relations_new++; }
    rel_store.flush();

    RelationSequenceStore rel_seq_store(db_);
    for (const auto& r : relations) for (const auto& item : r.sequence) {
        std::vector<uint8_t> data; data.push_back(0x02); // RelationSequence tag
        data.insert(data.end(), r.id.begin(), r.id.end()); data.insert(data.end(), item.id.begin(), item.id.end());
        uint32_t ord = item.ordinal; data.insert(data.end(), reinterpret_cast<uint8_t*>(&ord), reinterpret_cast<uint8_t*>(&ord) + sizeof(ord));
        rel_seq_store.store({BLAKE3Pipeline::hash(data), r.id, item.id, item.ordinal, item.occurrences});
    }
    rel_seq_store.flush();

    RelationRatingStore rating_store(db_);
    for (const auto& r : relations) rating_store.store({r.id, 1, r.initial_elo, 32.0});
    rating_store.flush();

    RelationEvidenceStore ev_store(db_);
    for (const auto& r : relations) {
        std::vector<uint8_t> d; d.insert(d.end(), content_id.begin(), content_id.end()); d.insert(d.end(), r.id.begin(), r.id.end());
        ev_store.store({BLAKE3Pipeline::hash(d), content_id, r.id, true, 1000.0, (r.initial_elo - 800.0) / 700.0});
        stats.evidence_count++;
    }
    ev_store.flush();
    txn.commit();
}

void TextIngester::load_global_caches() {
    std::cout << "Loading global caches from substrate..." << std::endl;
    db_.query("SELECT id FROM hartonomous.physicality", [&](const std::vector<std::string>& row) { seen_physicality_ids_.insert(BLAKE3Pipeline::from_hex(row[0])); });
    db_.query("SELECT id FROM hartonomous.atom", [&](const std::vector<std::string>& row) { seen_atom_ids_.insert(BLAKE3Pipeline::from_hex(row[0])); });
    db_.query("SELECT id FROM hartonomous.composition", [&](const std::vector<std::string>& row) { seen_composition_ids_.insert(BLAKE3Pipeline::from_hex(row[0])); });
    db_.query("SELECT id FROM hartonomous.relation", [&](const std::vector<std::string>& row) { seen_relation_ids_.insert(BLAKE3Pipeline::from_hex(row[0])); });
    std::cout << "  Caches loaded: " << seen_physicality_ids_.size() << " phys, " << seen_atom_ids_.size() << " atoms, " << seen_composition_ids_.size() << " comps, " << seen_relation_ids_.size() << " rels" << std::endl;
}

IngestionStats TextIngester::ingest(const std::string& text) {
    IngestionStats stats; stats.original_bytes = text.size();
    if (seen_physicality_ids_.empty()) load_global_caches();
    std::u32string utf32 = utf8_to_utf32(text);
    auto cid = create_content_record(text);
    auto atoms = extract_atoms(utf32);
    stats.atoms_total = atoms.size();
    std::unordered_map<BLAKE3Pipeline::Hash, Atom, HashHasher> amap;
    for (const auto& atom : atoms) amap[atom.id] = atom;
    extractor_.clear(); extractor_.extract(utf32);
    stats.ngrams_extracted = extractor_.total_ngrams();
    stats.cooccurrences_found = extractor_.total_cooccurrences();
    auto comps = extract_compositions(utf32, amap);
    stats.compositions_total = comps.size();
    stats.ngrams_significant = comps.size();
    std::unordered_map<BLAKE3Pipeline::Hash, Composition, HashHasher> cmap;
    for (const auto& c : comps) cmap[c.id] = c;
    auto rels = extract_relations(cmap);
    stats.relations_total = rels.size();
    stats.cooccurrences_significant = rels.size();
    store_all(cid, atoms, comps, rels, stats);
    return stats;
}

IngestionStats TextIngester::ingest_file(const std::string& path) {
    std::ifstream file(path);
    if (!file) throw std::runtime_error("Failed to open file: " + path);
    std::ostringstream buffer; buffer << file.rdbuf();
    config_.source = path;
    return ingest(buffer.str());
}

}