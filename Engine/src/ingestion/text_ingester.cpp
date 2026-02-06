/**
 * @file text_ingester.cpp
 * @brief Text ingestion: Atoms → Compositions → Relations
 *
 * This IS the inference path. Every prompt, every conversation gets ingested.
 * Ingestion = training. No context window. The substrate remembers forever.
 */

#include <ingestion/text_ingester.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/content_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <storage/physicality_store.hpp>
#include <iostream>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <unordered_set>
#include <unordered_map>
#include <vector>
#include <chrono>

namespace Hartonomous {

using Clock = std::chrono::steady_clock;
static double ms_since(Clock::time_point t0) {
    return std::chrono::duration<double, std::milli>(Clock::now() - t0).count();
}

TextIngester::TextIngester(PostgresConnection& db, const IngestionConfig& config)
    : db_(db), config_(config), atom_lookup_(db) {}

void TextIngester::preload_atoms() {
    if (!atoms_preloaded_) {
        atom_lookup_.preload_all();
        atoms_preloaded_ = true;
    }
}

std::string TextIngester::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << (static_cast<unsigned>(hash[i]) & 0xFF);
    }
    return ss.str();
}

std::u32string TextIngester::utf8_to_utf32(const std::string& s) {
    std::u32string out;
    out.reserve(s.size());
    size_t i = 0;
    while (i < s.size()) {
        uint8_t c = s[i];
        char32_t cp = 0;
        size_t len = 0;
        if (c < 0x80) { cp = c; len = 1; }
        else if ((c >> 5) == 0x6) { cp = c & 0x1F; len = 2; }
        else if ((c >> 4) == 0xE) { cp = c & 0x0F; len = 3; }
        else if ((c >> 3) == 0x1E) { cp = c & 0x07; len = 4; }
        else { ++i; continue; } // Skip invalid
        for (size_t j = 1; j < len && i + j < s.size(); ++j) {
            uint8_t cc = s[i + j];
            if ((cc >> 6) != 0x2) { len = 1; break; }
            cp = (cp << 6) | (cc & 0x3F);
        }
        out.push_back(cp);
        i += len;
    }
    return out;
}

BLAKE3Pipeline::Hash TextIngester::create_content_record(const std::string& text, BLAKE3Pipeline::Hash* content_hash) {
    auto computed_hash = BLAKE3Pipeline::hash(text);
    if (content_hash) {
        *content_hash = computed_hash;
    }
    std::vector<uint8_t> id_data;
    id_data.push_back(0x43); // 'C' for Content
    id_data.insert(id_data.end(), computed_hash.begin(), computed_hash.end());
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    return BLAKE3Pipeline::hash(id_data);
}

IngestionStats TextIngester::ingest(const std::string& text) {
    auto t_total = Clock::now();
    IngestionStats stats;
    stats.original_bytes = text.size();

    // Content dedup: same content hash = already ingested, skip entirely
    auto content_hash = BLAKE3Pipeline::hash(text);
    auto existing = db_.query_single(
        "SELECT id::text FROM hartonomous.content WHERE contenthash = $1",
        {hash_to_uuid(content_hash)});
    if (existing.has_value()) {
        std::cout << "  Content already ingested (hash match), skipping." << std::endl;
        return stats;
    }

    // Ensure atoms are preloaded (one-time cost, cached forever)
    auto t0 = Clock::now();
    preload_atoms();
    double t_atoms = ms_since(t0);

    // Phase 1: UTF-8 → UTF-32 → suffix array composition discovery
    t0 = Clock::now();
    std::u32string utf32 = utf8_to_utf32(text);
    NGramConfig ng_config;
    ng_config.min_n = config_.min_ngram_size;
    ng_config.max_n = config_.max_ngram_size;
    ng_config.min_frequency = config_.min_frequency;
    extractor_ = NGramExtractor(ng_config);
    extractor_.extract(utf32);
    double t_extract = ms_since(t0);

    // Phase 2: Significant compositions
    t0 = Clock::now();
    auto sig_ngrams = extractor_.significant_ngrams();
    stats.ngrams_extracted = extractor_.total_ngrams();
    stats.ngrams_significant = sig_ngrams.size();

    // Build composition records for each significant n-gram
    struct CompInfo {
        BLAKE3Pipeline::Hash comp_id;
        BLAKE3Pipeline::Hash phys_id;
        Eigen::Vector4d centroid;
        std::vector<Eigen::Vector4d> trajectory;
        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        uint32_t n;  // length in codepoints
    };
    std::unordered_map<BLAKE3Pipeline::Hash, CompInfo, HashHasher> comp_map;
    // Map ngram hash → composition id
    std::unordered_map<BLAKE3Pipeline::Hash, BLAKE3Pipeline::Hash, HashHasher> ngram_to_comp;

    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;

    for (const auto* ng : sig_ngrams) {
        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        std::vector<Eigen::Vector4d> positions;

        for (char32_t cp : ng->text) {
            auto info = atom_lookup_.lookup(cp);
            if (info) {
                atom_ids.push_back(info->id);
                positions.push_back(info->position);
            }
        }
        if (atom_ids.empty()) continue;

        // Composition ID = BLAKE3(0x43 + atom_id sequence)
        size_t comp_data_len = 1 + atom_ids.size() * 16;
        std::vector<uint8_t> comp_data(comp_data_len);
        comp_data[0] = 0x43;
        for (size_t k = 0; k < atom_ids.size(); ++k)
            std::memcpy(comp_data.data() + 1 + k * 16, atom_ids[k].data(), 16);
        auto comp_id = BLAKE3Pipeline::hash(comp_data.data(), comp_data_len);

        ngram_to_comp[ng->hash] = comp_id;

        if (comp_map.find(comp_id) != comp_map.end()) continue;

        Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
        for (const auto& p : positions) centroid += p;
        centroid /= static_cast<double>(positions.size());
        double norm = centroid.norm();
        if (norm > 1e-10) centroid /= norm;
        else centroid = Eigen::Vector4d(1, 0, 0, 0);

        uint8_t phys_data[33];
        phys_data[0] = 0x50;
        std::memcpy(phys_data + 1, centroid.data(), sizeof(double) * 4);
        auto phys_id = BLAKE3Pipeline::hash(phys_data, 33);

        comp_map[comp_id] = {comp_id, phys_id, centroid, positions, atom_ids, ng->n};
    }
    double t_compose = ms_since(t0);

    // Phase 3: Greedy tiling → adjacency-based relations
    // Walk the text left-to-right. At each position, find the longest composition.
    // Adjacent compositions in the tiling form relations.
    t0 = Clock::now();

    // Build a position→longest-composition lookup from the extractor's position data
    // For each significant ngram, record all its positions with length
    struct PosComp {
        BLAKE3Pipeline::Hash ngram_hash;
        uint32_t length;
    };
    // pos → list of compositions starting here (we'll pick longest)
    std::unordered_map<uint32_t, PosComp> best_at_pos;
    for (const auto* ng : sig_ngrams) {
        auto comp_it = ngram_to_comp.find(ng->hash);
        if (comp_it == ngram_to_comp.end()) continue;
        
        for (uint32_t pos : ng->positions) {
            auto it = best_at_pos.find(pos);
            if (it == best_at_pos.end() || ng->n > it->second.length) {
                best_at_pos[pos] = {ng->hash, ng->n};
            }
        }
    }

    // Walk text greedily: always take the longest match
    struct TileEntry {
        BLAKE3Pipeline::Hash comp_id;
        uint32_t position;
        uint32_t length;
    };
    std::vector<TileEntry> tiling;
    tiling.reserve(utf32.size() / 3); // rough estimate

    uint32_t pos = 0;
    while (pos < utf32.size()) {
        auto it = best_at_pos.find(pos);
        if (it != best_at_pos.end()) {
            auto comp_it = ngram_to_comp.find(it->second.ngram_hash);
            if (comp_it != ngram_to_comp.end()) {
                tiling.push_back({comp_it->second, pos, it->second.length});
                pos += it->second.length;
                continue;
            }
        }
        // No composition found — single atom (unigram)
        char32_t cp = utf32[pos];
        auto hash = BLAKE3Pipeline::hash(reinterpret_cast<const uint8_t*>(&cp), sizeof(char32_t));
        // Look up the unigram composition
        auto ng_it = ngram_to_comp.find(hash);
        if (ng_it != ngram_to_comp.end()) {
            tiling.push_back({ng_it->second, pos, 1});
        }
        pos++;
    }

    std::cout << "  Tiling: " << tiling.size() << " tiles covering " << utf32.size()
              << " codepoints" << std::endl;

    // Count adjacent composition pairs
    struct AdjPair {
        BLAKE3Pipeline::Hash comp_a;
        BLAKE3Pipeline::Hash comp_b;
    };
    struct AdjPairHash {
        size_t operator()(const AdjPair& p) const {
            size_t h = 0;
            for (int i = 0; i < 4; ++i) h ^= std::hash<uint32_t>{}(reinterpret_cast<const uint32_t*>(p.comp_a.data())[i]) << (i * 8);
            for (int i = 0; i < 4; ++i) h ^= std::hash<uint32_t>{}(reinterpret_cast<const uint32_t*>(p.comp_b.data())[i]) << (i * 8 + 4);
            return h;
        }
    };
    struct AdjPairEq {
        bool operator()(const AdjPair& a, const AdjPair& b) const {
            return a.comp_a == b.comp_a && a.comp_b == b.comp_b;
        }
    };

    // Adjacency pairs: count + total gap distance
    struct AdjStats {
        uint32_t count = 0;
        double total_distance = 0.0;
    };
    std::unordered_map<AdjPair, AdjStats, AdjPairHash, AdjPairEq> adj_pairs;

    for (size_t i = 0; i + 1 < tiling.size(); ++i) {
        const auto& a = tiling[i];
        const auto& b = tiling[i + 1];
        if (a.comp_id == b.comp_id) continue; // Self-relation not useful

        // Canonical order for deterministic relation ID
        bool a_first = std::memcmp(a.comp_id.data(), b.comp_id.data(), 16) < 0;
        AdjPair key = a_first ? AdjPair{a.comp_id, b.comp_id} : AdjPair{b.comp_id, a.comp_id};
        
        uint32_t gap = (b.position >= a.position + a.length) ? (b.position - a.position - a.length) : 0;
        adj_pairs[key].count++;
        adj_pairs[key].total_distance += gap;
    }

    std::cout << "  Adjacency: " << adj_pairs.size() << " unique pairs from "
              << tiling.size() - 1 << " transitions" << std::endl;

    double t_relations = ms_since(t0);

    // Phase 4: Build persistence records
    t0 = Clock::now();
    std::vector<PhysicalityRecord> phys_records;
    std::vector<CompositionRecord> comp_records;
    std::vector<CompositionSequenceRecord> seq_records;
    std::vector<RelationRecord> rel_records;
    std::vector<RelationSequenceRecord> rel_seq_records;
    std::vector<RelationRatingRecord> rating_records;
    std::vector<RelationEvidenceRecord> ev_records;

    for (auto& [comp_id, ci] : comp_map) {
        if (phys_seen.insert(ci.phys_id).second) {
            Eigen::Vector4d hc;
            for (int k = 0; k < 4; ++k) hc[k] = (ci.centroid[k] + 1.0) / 2.0;
            phys_records.push_back({ci.phys_id, HilbertCurve4D::encode(hc), ci.centroid, ci.trajectory});
        }
        comp_records.push_back({comp_id, ci.phys_id});
        stats.compositions_new++;

        for (size_t i = 0; i < ci.atom_ids.size(); ) {
            uint32_t ordinal = static_cast<uint32_t>(i);
            uint32_t occurrences = 1;
            while (i + occurrences < ci.atom_ids.size() && ci.atom_ids[i + occurrences] == ci.atom_ids[i]) ++occurrences;
            uint8_t seq_data[37];
            seq_data[0] = 0x53;
            std::memcpy(seq_data + 1, comp_id.data(), 16);
            std::memcpy(seq_data + 17, ci.atom_ids[i].data(), 16);
            std::memcpy(seq_data + 33, &ordinal, 4);
            seq_records.push_back({ BLAKE3Pipeline::hash(seq_data, 37), comp_id, ci.atom_ids[i], ordinal, occurrences });
            i += occurrences;
        }
    }

    // Content ID for evidence
    std::vector<uint8_t> id_data;
    id_data.push_back(0x43);
    id_data.insert(id_data.end(), content_hash.begin(), content_hash.end());
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    auto content_id = BLAKE3Pipeline::hash(id_data);

    // Build relation records from adjacency pairs
    for (const auto& [pair, adj] : adj_pairs) {
        uint8_t rel_input[33];
        rel_input[0] = 0x52;
        std::memcpy(rel_input + 1, pair.comp_a.data(), 16);
        std::memcpy(rel_input + 17, pair.comp_b.data(), 16);
        auto rel_id = BLAKE3Pipeline::hash(rel_input, 33);

        auto ci_a = comp_map.find(pair.comp_a);
        auto ci_b = comp_map.find(pair.comp_b);
        if (ci_a == comp_map.end() || ci_b == comp_map.end()) continue;

        // Relation centroid = midpoint of the two composition centroids on S³
        Eigen::Vector4d rel_centroid = (ci_a->second.centroid + ci_b->second.centroid) * 0.5;
        double norm = rel_centroid.norm();
        if (norm > 1e-10) rel_centroid /= norm;
        else rel_centroid = Eigen::Vector4d(1, 0, 0, 0);

        uint8_t rel_phys_data[33];
        rel_phys_data[0] = 0x50;
        std::memcpy(rel_phys_data + 1, rel_centroid.data(), sizeof(double) * 4);
        auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data, 33);

        if (phys_seen.insert(rel_phys_id).second) {
            Eigen::Vector4d hc;
            for (int k = 0; k < 4; ++k) hc[k] = (rel_centroid[k] + 1.0) / 2.0;
            std::vector<Eigen::Vector4d> rel_trajectory = {ci_a->second.centroid, ci_b->second.centroid};
            phys_records.push_back({rel_phys_id, HilbertCurve4D::encode(hc), rel_centroid, rel_trajectory});
        }
        rel_records.push_back({rel_id, rel_phys_id});
        stats.relations_new++;

        for (uint32_t k = 0; k < 2; ++k) {
            const auto& cid = (k == 0) ? pair.comp_a : pair.comp_b;
            uint8_t rs_data[37];
            rs_data[0] = 0x54;
            std::memcpy(rs_data + 1, rel_id.data(), 16);
            std::memcpy(rs_data + 17, cid.data(), 16);
            std::memcpy(rs_data + 33, &k, 4);
            rel_seq_records.push_back({ BLAKE3Pipeline::hash(rs_data, 37), rel_id, cid, k, 1 });
        }

        // Signal strength: 1/(1+avg_gap). Adjacent (gap=0) = 1.0, gap=1 = 0.5, etc.
        double avg_gap = adj.total_distance / adj.count;
        double signal = std::min(1.0, 1.0 / (1.0 + avg_gap));
        double src_rating = 1200.0 + 400.0 * std::log2(static_cast<double>(adj.count) + 1.0);

        uint8_t ev_data[32];
        std::memcpy(ev_data, content_id.data(), 16);
        std::memcpy(ev_data + 16, rel_id.data(), 16);
        ev_records.push_back({ BLAKE3Pipeline::hash(ev_data, 32), content_id, rel_id, true, src_rating, signal });
        stats.evidence_count++;

        rating_records.push_back({ rel_id, static_cast<uint64_t>(adj.count), src_rating, 32.0 });
    }

    // Phase 5: Persist — single transaction, COPY-based bulk with ON CONFLICT
    double t_build = ms_since(t0);
    t0 = Clock::now();
    {
        PostgresConnection::Transaction txn(db_);
        ContentStore(db_).store({content_id, config_.tenant_id, config_.user_id, config_.content_type,
                                 content_hash, stats.original_bytes, config_.mime_type, config_.language,
                                 config_.source, config_.encoding});

        { PhysicalityStore s(db_, true, true); for (auto& r : phys_records) s.store(r); s.flush(); }
        { CompositionStore s(db_, true, true); for (auto& r : comp_records) s.store(r); s.flush(); }
        { CompositionSequenceStore s(db_, true, true); for (auto& r : seq_records) s.store(r); s.flush(); }
        { RelationStore s(db_, true, true); for (auto& r : rel_records) s.store(r); s.flush(); }
        { RelationSequenceStore s(db_, true, true); for (auto& r : rel_seq_records) s.store(r); s.flush(); }
        { RelationRatingStore s(db_, true); for (auto& r : rating_records) s.store(r); s.flush(); }
        { RelationEvidenceStore s(db_, true, true); for (auto& r : ev_records) s.store(r); s.flush(); }
        txn.commit();
    }
    double t_persist = ms_since(t0);

    stats.compositions_total = comp_map.size();
    stats.relations_total = adj_pairs.size();
    stats.atoms_total = 0;
    for (const auto& [id, ci] : comp_map) stats.atoms_total += ci.atom_ids.size();

    double t_total_ms = ms_since(t_total);
    std::cout << "  Timing: atoms=" << std::fixed << std::setprecision(0) << t_atoms
              << "ms extract=" << t_extract << "ms compose=" << t_compose
              << "ms relations=" << t_relations << "ms build=" << t_build
              << "ms persist=" << t_persist << "ms total=" << t_total_ms << "ms" << std::endl;
    std::cout << "  Compositions: " << stats.ngrams_extracted << " discovered, "
              << stats.ngrams_significant << " significant → "
              << stats.compositions_new << " stored" << std::endl;
    std::cout << "  Relations: " << adj_pairs.size() << " adjacency pairs → "
              << stats.relations_new << " relations, " << stats.evidence_count << " evidence" << std::endl;

    return stats;
}

IngestionStats TextIngester::ingest_file(const std::string& path) {
    std::ifstream file(path);
    if (!file) throw std::runtime_error("Failed to open: " + path);
    std::ostringstream b;
    b << file.rdbuf();
    config_.source = path;
    return ingest(b.str());
}

// Stubs
std::vector<TextIngester::Atom> TextIngester::extract_atoms(const std::u32string&) { return {}; }
std::vector<TextIngester::Composition> TextIngester::extract_compositions(const std::u32string&, const std::unordered_map<BLAKE3Pipeline::Hash, Atom, HashHasher>&) { return {}; }
std::vector<TextIngester::Relation> TextIngester::extract_relations(const std::unordered_map<BLAKE3Pipeline::Hash, Composition, HashHasher>&) { return {}; }
TextIngester::Physicality TextIngester::compute_physicality(const Vec4&) { return {}; }
void TextIngester::store_all(const BLAKE3Pipeline::Hash&, const std::vector<Atom>&, const std::vector<Composition>&, const std::vector<Relation>&, IngestionStats&) {}
BLAKE3Pipeline::Hash TextIngester::compute_sequence_hash(const std::vector<SequenceItem>&, uint8_t) { return {}; }
Vec4 TextIngester::compute_centroid(const std::vector<Vec4>&) { return {}; }
std::string TextIngester::hash_to_hex(const BLAKE3Pipeline::Hash& hash) { return BLAKE3Pipeline::to_hex(hash); }

std::string TextIngester::utf32_to_utf8(const std::u32string& s) {
    std::string out;
    out.reserve(s.size() * 4);
    for (char32_t cp : s) {
        if (cp < 0x80) { out += static_cast<char>(cp); }
        else if (cp < 0x800) { out += static_cast<char>(0xC0 | (cp >> 6)); out += static_cast<char>(0x80 | (cp & 0x3F)); }
        else if (cp < 0x10000) { out += static_cast<char>(0xE0 | (cp >> 12)); out += static_cast<char>(0x80 | ((cp >> 6) & 0x3F)); out += static_cast<char>(0x80 | (cp & 0x3F)); }
        else { out += static_cast<char>(0xF0 | (cp >> 18)); out += static_cast<char>(0x80 | ((cp >> 12) & 0x3F)); out += static_cast<char>(0x80 | ((cp >> 6) & 0x3F)); out += static_cast<char>(0x80 | (cp & 0x3F)); }
    }
    return out;
}

} // namespace Hartonomous
