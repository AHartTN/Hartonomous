/**
 * @file text_ingester.cpp
 * @brief Universal text ingestion implementation
 *
 * Full Merkle DAG decomposition:
 *   Content (root) → Relations (co-occurrences) → Compositions (n-grams) → Atoms (codepoints)
 */

#include <ingestion/text_ingester.hpp>
#include <storage/physicality_store.hpp>
#include <storage/atom_store.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/content_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <fstream>
#include <codecvt>
#include <locale>
#include <iomanip>
#include <sstream>
#include <algorithm>
#include <cmath>

namespace Hartonomous {

TextIngester::TextIngester(PostgresConnection& db, const IngestionConfig& config)
    : db_(db), config_(config) {

    // Configure n-gram extractor
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
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (size_t i = 0; i < hash.size(); ++i) {
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

std::u32string TextIngester::utf8_to_utf32(const std::string& utf8) {
    std::wstring_convert<std::codecvt_utf8<char32_t>, char32_t> converter;
    return converter.from_bytes(utf8);
}

Vec4 TextIngester::compute_centroid(const std::vector<Vec4>& positions) {
    if (positions.empty()) {
        return Vec4(1.0, 0.0, 0.0, 0.0);
    }

    Vec4 sum = Vec4::Zero();
    for (const auto& pos : positions) {
        sum += pos;
    }
    Vec4 avg = sum / static_cast<double>(positions.size());

    double norm = avg.norm();
    if (norm < 1e-10) {
        return Vec4(1.0, 0.0, 0.0, 0.0);
    }
    return avg / norm;
}

TextIngester::Physicality TextIngester::compute_physicality(const Vec4& centroid) {
    Physicality phys;
    phys.centroid = centroid;

    // Compute Hilbert index
    Eigen::Vector4d hypercube;
    for (int i = 0; i < 4; ++i) {
        hypercube[i] = (centroid[i] + 1.0) / 2.0;
    }
    phys.hilbert_index = HilbertCurve4D::encode(hypercube);

    // Physicality ID from centroid bytes
    std::vector<uint8_t> centroid_bytes(4 * sizeof(double));
    std::memcpy(centroid_bytes.data(), centroid.data(), 4 * sizeof(double));
    phys.id = BLAKE3Pipeline::hash(centroid_bytes);

    return phys;
}

BLAKE3Pipeline::Hash TextIngester::compute_sequence_hash(const std::vector<SequenceItem>& sequence) {
    std::vector<uint8_t> data;
    data.reserve(sequence.size() * 36);  // 32 bytes hash + 4 bytes ordinal

    for (const auto& item : sequence) {
        data.insert(data.end(), item.id.begin(), item.id.end());
        uint32_t ord = item.ordinal;
        data.insert(data.end(),
            reinterpret_cast<uint8_t*>(&ord),
            reinterpret_cast<uint8_t*>(&ord) + sizeof(ord));
    }

    return BLAKE3Pipeline::hash(data);
}

BLAKE3Pipeline::Hash TextIngester::create_content_record(const std::string& text) {
    // Content ID is hash of the raw bytes
    auto content_hash = BLAKE3Pipeline::hash(text);

    // Content record ID (different from content hash - includes metadata)
    std::vector<uint8_t> id_data;
    id_data.insert(id_data.end(), content_hash.begin(), content_hash.end());
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    auto content_id = BLAKE3Pipeline::hash(id_data);

    return content_id;
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

        // Physicality ID from centroid
        std::vector<uint8_t> centroid_bytes(4 * sizeof(double));
        std::memcpy(centroid_bytes.data(), proj.s3_position.data(), 4 * sizeof(double));
        atom.physicality.id = BLAKE3Pipeline::hash(centroid_bytes);

        atoms.push_back(atom);
    }

    return atoms;
}

std::vector<TextIngester::Composition> TextIngester::extract_compositions(
    [[maybe_unused]] const std::u32string& text,
    const std::unordered_map<std::string, Atom>& atom_map) {

    std::vector<Composition> compositions;

    // Get significant n-grams from extractor
    auto significant = extractor_.significant_ngrams();

    for (const NGram* ngram : significant) {
        Composition comp;
        comp.text = ngram->text;
        comp.id = ngram->hash;

        // Build atom sequence and collect positions for centroid
        std::vector<Vec4> atom_positions;
        uint32_t ordinal = 0;

        for (char32_t cp : ngram->text) {
            auto proj = CodepointProjection::project(cp);
            std::string atom_hex = hash_to_hex(proj.hash);

            auto atom_it = atom_map.find(atom_hex);
            if (atom_it != atom_map.end()) {
                SequenceItem item;
                item.id = atom_it->second.id;
                item.ordinal = ordinal++;
                item.occurrences = 1;  // Within this n-gram
                comp.sequence.push_back(item);
                atom_positions.push_back(atom_it->second.physicality.centroid);
            }
        }

        // Compute composition physicality from constituent atoms
        comp.physicality = compute_physicality(compute_centroid(atom_positions));

        compositions.push_back(comp);
    }

    return compositions;
}

std::vector<TextIngester::Relation> TextIngester::extract_relations(
    const std::unordered_map<std::string, Composition>& comp_map) {

    std::vector<Relation> relations;

    // Get significant co-occurrences
    auto significant = extractor_.significant_cooccurrences(config_.min_cooccurrence);

    for (const CoOccurrence* cooc : significant) {
        std::string hash_a = hash_to_hex(cooc->ngram_a);
        std::string hash_b = hash_to_hex(cooc->ngram_b);

        auto it_a = comp_map.find(hash_a);
        auto it_b = comp_map.find(hash_b);

        // Only create relations between compositions we've stored
        if (it_a == comp_map.end() || it_b == comp_map.end()) continue;

        Relation rel;

        // Sequence: the two compositions that co-occur
        SequenceItem item_a, item_b;
        item_a.id = cooc->ngram_a;
        item_a.ordinal = cooc->is_forward() ? 0 : 1;
        item_a.occurrences = cooc->count;

        item_b.id = cooc->ngram_b;
        item_b.ordinal = cooc->is_forward() ? 1 : 0;
        item_b.occurrences = cooc->count;

        rel.sequence.push_back(item_a);
        rel.sequence.push_back(item_b);

        // Compute relation ID from sequence
        rel.id = compute_sequence_hash(rel.sequence);

        // Compute physicality from the two compositions
        std::vector<Vec4> positions = {
            it_a->second.physicality.centroid,
            it_b->second.physicality.centroid
        };
        rel.physicality = compute_physicality(compute_centroid(positions));

        // Initial ELO based on signal strength (frequency and proximity)
        // Signal strength of 1.0 → ELO 1500 (strong relation)
        // Signal strength of 0.1 → ELO 1000 (weak relation)
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

    // === 1. Store Content (root node) ===
    ContentStore content_store(db_);
    ContentRecord content_rec;
    content_rec.id = content_id;
    content_rec.tenant_id = config_.tenant_id;
    content_rec.user_id = config_.user_id;
    content_rec.content_type = config_.content_type;
    content_rec.content_hash = content_id;  // Using ID as hash for simplicity
    content_rec.content_size = stats.original_bytes;
    content_rec.mime_type = config_.mime_type;
    content_rec.language = config_.language;
    content_rec.source = config_.source;
    content_rec.encoding = config_.encoding;
    content_store.store(content_rec);
    content_store.flush();

    // === 2. Store Physicalities (all at once to satisfy FK constraints) ===
    PhysicalityStore phys_store(db_);

    for (const auto& a : atoms) {
        std::string phys_uuid = hash_to_uuid(a.physicality.id);
        if (seen_physicality_ids_.find(phys_uuid) == seen_physicality_ids_.end()) {
            PhysicalityRecord phys_rec;
            phys_rec.id = a.physicality.id;
            phys_rec.hilbert_index = a.physicality.hilbert_index;
            phys_rec.centroid = a.physicality.centroid;
            phys_store.store(phys_rec);
            seen_physicality_ids_.insert(phys_uuid);
        }
    }

    for (const auto& c : compositions) {
        std::string phys_uuid = hash_to_uuid(c.physicality.id);
        if (seen_physicality_ids_.find(phys_uuid) == seen_physicality_ids_.end()) {
            PhysicalityRecord phys_rec;
            phys_rec.id = c.physicality.id;
            phys_rec.hilbert_index = c.physicality.hilbert_index;
            phys_rec.centroid = c.physicality.centroid;
            phys_store.store(phys_rec);
            seen_physicality_ids_.insert(phys_uuid);
        }
    }

    for (const auto& r : relations) {
        std::string phys_uuid = hash_to_uuid(r.physicality.id);
        if (seen_physicality_ids_.find(phys_uuid) == seen_physicality_ids_.end()) {
            PhysicalityRecord phys_rec;
            phys_rec.id = r.physicality.id;
            phys_rec.hilbert_index = r.physicality.hilbert_index;
            phys_rec.centroid = r.physicality.centroid;
            phys_store.store(phys_rec);
            seen_physicality_ids_.insert(phys_uuid);
        }
    }
    phys_store.flush();

    // === 3. Store Atoms ===
    AtomStore atom_store(db_);
    for (const auto& a : atoms) {
        std::string atom_uuid = hash_to_uuid(a.id);
        if (seen_atom_ids_.find(atom_uuid) == seen_atom_ids_.end()) {
            AtomRecord atom_rec;
            atom_rec.id = a.id;
            atom_rec.physicality_id = a.physicality.id;
            atom_rec.codepoint = a.codepoint;
            atom_store.store(atom_rec);
            seen_atom_ids_.insert(atom_uuid);
            stats.atoms_new++;
        }
    }
    atom_store.flush();

    // === 4. Store Compositions ===
    CompositionStore comp_store(db_);
    for (const auto& c : compositions) {
        std::string comp_uuid = hash_to_uuid(c.id);
        if (seen_composition_ids_.find(comp_uuid) == seen_composition_ids_.end()) {
            CompositionRecord comp_rec;
            comp_rec.id = c.id;
            comp_rec.physicality_id = c.physicality.id;
            comp_store.store(comp_rec);
            seen_composition_ids_.insert(comp_uuid);
            stats.compositions_new++;
        }
    }
    comp_store.flush();

    // === 5. Store Composition Sequences ===
    CompositionSequenceStore comp_seq_store(db_);
    for (const auto& c : compositions) {
        for (const auto& item : c.sequence) {
            CompositionSequenceRecord seq_rec;
            // Unique ID for sequence entry
            std::vector<uint8_t> seq_data;
            seq_data.insert(seq_data.end(), c.id.begin(), c.id.end());
            seq_data.insert(seq_data.end(), item.id.begin(), item.id.end());
            uint32_t ord = item.ordinal;
            seq_data.insert(seq_data.end(),
                reinterpret_cast<uint8_t*>(&ord),
                reinterpret_cast<uint8_t*>(&ord) + sizeof(ord));
            seq_rec.id = BLAKE3Pipeline::hash(seq_data);
            seq_rec.composition_id = c.id;
            seq_rec.atom_id = item.id;
            seq_rec.ordinal = item.ordinal;
            seq_rec.occurrences = item.occurrences;
            comp_seq_store.store(seq_rec);
        }
    }
    comp_seq_store.flush();

    // === 6. Store Relations ===
    RelationStore rel_store(db_);
    for (const auto& r : relations) {
        std::string rel_uuid = hash_to_uuid(r.id);
        if (seen_relation_ids_.find(rel_uuid) == seen_relation_ids_.end()) {
            RelationRecord rel_rec;
            rel_rec.id = r.id;
            rel_rec.physicality_id = r.physicality.id;
            rel_store.store(rel_rec);
            seen_relation_ids_.insert(rel_uuid);
            stats.relations_new++;
        }
    }
    rel_store.flush();

    // === 7. Store Relation Sequences ===
    RelationSequenceStore rel_seq_store(db_);
    for (const auto& r : relations) {
        for (const auto& item : r.sequence) {
            RelationSequenceRecord seq_rec;
            std::vector<uint8_t> seq_data;
            seq_data.insert(seq_data.end(), r.id.begin(), r.id.end());
            seq_data.insert(seq_data.end(), item.id.begin(), item.id.end());
            uint32_t ord = item.ordinal;
            seq_data.insert(seq_data.end(),
                reinterpret_cast<uint8_t*>(&ord),
                reinterpret_cast<uint8_t*>(&ord) + sizeof(ord));
            seq_rec.id = BLAKE3Pipeline::hash(seq_data);
            seq_rec.relation_id = r.id;
            seq_rec.composition_id = item.id;
            seq_rec.ordinal = item.ordinal;
            seq_rec.occurrences = item.occurrences;
            rel_seq_store.store(seq_rec);
        }
    }
    rel_seq_store.flush();

    // === 8. Store Relation Ratings (ELO) ===
    RelationRatingStore rating_store(db_);
    for (const auto& r : relations) {
        RelationRatingRecord rating_rec;
        rating_rec.relation_id = r.id;
        rating_rec.observations = 1;
        rating_rec.rating_value = r.initial_elo;
        rating_rec.k_factor = 32.0;  // High K for new relations
        rating_store.store(rating_rec);
    }
    rating_store.flush();

    // === 9. Store Relation Evidence (link to Content) ===
    RelationEvidenceStore evidence_store(db_);
    for (const auto& r : relations) {
        RelationEvidenceRecord ev_rec;
        // Evidence ID: hash of content_id + relation_id
        std::vector<uint8_t> ev_data;
        ev_data.insert(ev_data.end(), content_id.begin(), content_id.end());
        ev_data.insert(ev_data.end(), r.id.begin(), r.id.end());
        ev_rec.id = BLAKE3Pipeline::hash(ev_data);
        ev_rec.content_id = content_id;
        ev_rec.relation_id = r.id;
        ev_rec.is_valid = true;
        ev_rec.source_rating = 1000.0;  // Default source rating
        // Signal strength based on co-occurrence statistics
        ev_rec.signal_strength = (r.initial_elo - 800.0) / 700.0;
        evidence_store.store(ev_rec);
        stats.evidence_count++;
    }
    evidence_store.flush();

    txn.commit();
}

IngestionStats TextIngester::ingest(const std::string& text) {
    IngestionStats stats;
    stats.original_bytes = text.size();

    // Convert to UTF-32 for processing
    std::u32string utf32_text = utf8_to_utf32(text);

    // Phase 1: Create Content record (root)
    auto content_id = create_content_record(text);

    // Phase 2: Extract atoms (unique codepoints)
    auto atoms = extract_atoms(utf32_text);
    stats.atoms_total = atoms.size();

    // Build atom lookup map
    std::unordered_map<std::string, Atom> atom_map;
    for (const auto& atom : atoms) {
        atom_map[hash_to_hex(atom.id)] = atom;
    }

    // Phase 3: Run n-gram extraction
    extractor_.clear();
    extractor_.extract(utf32_text);
    stats.ngrams_extracted = extractor_.total_ngrams();
    stats.cooccurrences_found = extractor_.total_cooccurrences();

    // Phase 4: Extract compositions (significant n-grams)
    auto compositions = extract_compositions(utf32_text, atom_map);
    stats.compositions_total = compositions.size();
    stats.ngrams_significant = compositions.size();

    // Build composition lookup map
    std::unordered_map<std::string, Composition> comp_map;
    for (const auto& comp : compositions) {
        comp_map[hash_to_hex(comp.id)] = comp;
    }

    // Phase 5: Extract relations (from co-occurrences)
    auto relations = extract_relations(comp_map);
    stats.relations_total = relations.size();
    stats.cooccurrences_significant = relations.size();

    // Phase 6: Store everything
    store_all(content_id, atoms, compositions, relations, stats);

    return stats;
}

IngestionStats TextIngester::ingest_file(const std::string& path) {
    std::ifstream file(path);
    if (!file) throw std::runtime_error("Failed to open file: " + path);
    std::ostringstream buffer;
    buffer << file.rdbuf();
    config_.source = path;
    return ingest(buffer.str());
}

}
