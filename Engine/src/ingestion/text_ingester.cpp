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

// Token structure: a word/subword made of atoms
struct Token {
    std::u32string codepoints;           // The actual codepoints
    BLAKE3Pipeline::Hash comp_id;        // Composition hash
    BLAKE3Pipeline::Hash phys_id;        // Physicality hash
    Eigen::Vector4d centroid;            // S³ centroid position
    std::vector<Eigen::Vector4d> trajectory; // 4D Trajectory
    std::vector<BLAKE3Pipeline::Hash> atom_ids;  // Atom IDs in order
};

// Tokenize text into words (whitespace/punctuation separated)
static std::vector<std::u32string> tokenize(const std::u32string& text) {
    std::vector<std::u32string> tokens;
    std::u32string current;

    for (char32_t cp : text) {
        // Check if word boundary (space, newline, common punctuation)
        bool is_boundary = (cp == ' ' || cp == '\t' || cp == '\n' || cp == '\r' ||
                           cp == '.' || cp == ',' || cp == '!' || cp == '?' ||
                           cp == ';' || cp == ':' || cp == '"' || cp == '\'' ||
                           cp == '(' || cp == ')' || cp == '[' || cp == ']' ||
                           cp == '{' || cp == '}' || cp == '-' || cp == 0);

        if (is_boundary) {
            if (!current.empty()) {
                tokens.push_back(current);
                current.clear();
            }
            // Include punctuation as its own token (single character compositions)
            if (cp != ' ' && cp != '\t' && cp != '\n' && cp != '\r' && cp != 0) {
                tokens.push_back({cp});
            }
        } else {
            current.push_back(cp);
        }
    }
    if (!current.empty()) {
        tokens.push_back(current);
    }
    return tokens;
}

IngestionStats TextIngester::ingest(const std::string& text) {
    auto t_total = Clock::now();
    IngestionStats stats;
    stats.original_bytes = text.size();

    // Ensure atoms are preloaded (one-time cost, cached forever)
    auto t0 = Clock::now();
    preload_atoms();
    double t_atoms = ms_since(t0);

    // Phase 1: UTF-8 → UTF-32 → tokens
    t0 = Clock::now();
    std::u32string utf32 = utf8_to_utf32(text);
    auto raw_tokens = tokenize(utf32);
    double t_tokenize = ms_since(t0);

    // Phase 2: Build compositions from token atoms (all lookups from cached atoms)
    t0 = Clock::now();
    std::unordered_map<BLAKE3Pipeline::Hash, Token, HashHasher> token_map;
    std::vector<BLAKE3Pipeline::Hash> token_sequence;
    token_sequence.reserve(raw_tokens.size());

    // Session-local dedup — no DB query needed, stores handle ON CONFLICT
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> session_comp_ids;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> session_rel_ids;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;

    for (const auto& tok_cps : raw_tokens) {
        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        std::vector<Eigen::Vector4d> positions;

        for (char32_t cp : tok_cps) {
            auto info = atom_lookup_.lookup(cp);
            if (info) {
                atom_ids.push_back(info->id);
                positions.push_back(info->position);
            }
        }

        if (atom_ids.empty()) continue;

        size_t comp_data_len = 1 + atom_ids.size() * 16;
        uint8_t comp_buf[1 + 64 * 16]; // stack buffer for typical tokens (up to 64 atoms)
        uint8_t* comp_data = (comp_data_len <= sizeof(comp_buf)) ? comp_buf : new uint8_t[comp_data_len];
        comp_data[0] = 0x43;
        for (size_t k = 0; k < atom_ids.size(); ++k)
            std::memcpy(comp_data + 1 + k * 16, atom_ids[k].data(), 16);
        auto comp_id = BLAKE3Pipeline::hash(comp_data, comp_data_len);
        if (comp_data != comp_buf) delete[] comp_data;

        token_sequence.push_back(comp_id);

        if (token_map.find(comp_id) == token_map.end()) {
            Token tok;
            tok.codepoints = tok_cps;
            tok.comp_id = comp_id;
            tok.atom_ids = atom_ids;
            tok.trajectory = positions;

            Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
            for (const auto& p : positions) centroid += p;
            centroid /= static_cast<double>(positions.size());
            double norm = centroid.norm();
            if (norm > 1e-10) centroid /= norm;
            else centroid = Eigen::Vector4d(1, 0, 0, 0);
            tok.centroid = centroid;

            uint8_t phys_data[33];
            phys_data[0] = 0x50;
            std::memcpy(phys_data + 1, centroid.data(), sizeof(double) * 4);
            tok.phys_id = BLAKE3Pipeline::hash(phys_data, 33);

            token_map[comp_id] = tok;
        }
    }
    double t_compose = ms_since(t0);

    // Phase 3: Collect composition records (new ones only within this session)
    t0 = Clock::now();
    std::vector<PhysicalityRecord> phys_records;
    std::vector<CompositionRecord> comp_records;
    std::vector<CompositionSequenceRecord> seq_records;
    std::vector<RelationRecord> rel_records;
    std::vector<RelationSequenceRecord> rel_seq_records;
    std::vector<RelationRatingRecord> rating_records;
    std::vector<RelationEvidenceRecord> ev_records;

    for (auto& [comp_id, tok] : token_map) {
        if (session_comp_ids.insert(comp_id).second) {
            if (phys_seen.insert(tok.phys_id).second) {
                Eigen::Vector4d hc;
                for (int k = 0; k < 4; ++k) hc[k] = (tok.centroid[k] + 1.0) / 2.0;
                phys_records.push_back({tok.phys_id, HilbertCurve4D::encode(hc), tok.centroid, tok.trajectory});
            }
            comp_records.push_back({comp_id, tok.phys_id});
            stats.compositions_new++;

            for (size_t i = 0; i < tok.atom_ids.size(); ) {
                uint32_t ordinal = static_cast<uint32_t>(i);
                uint32_t occurrences = 1;
                while (i + occurrences < tok.atom_ids.size() && tok.atom_ids[i + occurrences] == tok.atom_ids[i]) ++occurrences;
                uint8_t seq_data[37];
                seq_data[0] = 0x53;
                std::memcpy(seq_data + 1, comp_id.data(), 16);
                std::memcpy(seq_data + 17, tok.atom_ids[i].data(), 16);
                std::memcpy(seq_data + 33, &ordinal, 4);
                seq_records.push_back({ BLAKE3Pipeline::hash(seq_data, 37), comp_id, tok.atom_ids[i], ordinal, occurrences });
                i += occurrences;
            }
        }
    }

    // Phase 4: Relations (windowed co-occurrence) — aggregate per relation
    BLAKE3Pipeline::Hash content_hash;
    auto content_id = create_content_record(text, &content_hash);

    struct RelAgg { size_t count = 0; double total_signal = 0.0; };
    std::unordered_map<BLAKE3Pipeline::Hash, RelAgg, HashHasher> rel_agg;

    for (size_t i = 0; i < token_sequence.size(); ++i) {
        auto& comp1_id = token_sequence[i];
        size_t max_j = std::min(token_sequence.size(), i + config_.cooccurrence_window + 1);
        for (size_t j = i + 1; j < max_j; ++j) {
            auto& comp2_id = token_sequence[j];

            const auto& lo = (std::memcmp(comp1_id.data(), comp2_id.data(), 16) < 0) ? comp1_id : comp2_id;
            const auto& hi = (&lo == &comp1_id) ? comp2_id : comp1_id;
            uint8_t rel_input[33];
            rel_input[0] = 0x52;
            std::memcpy(rel_input + 1, lo.data(), 16);
            std::memcpy(rel_input + 17, hi.data(), 16);
            auto rel_id = BLAKE3Pipeline::hash(rel_input, 33);

            auto& agg = rel_agg[rel_id];
            agg.count++;
            agg.total_signal += 1.0 / static_cast<double>(j - i);

            if (session_rel_ids.insert(rel_id).second) {
                auto& tok1 = token_map[comp1_id];
                auto& tok2 = token_map[comp2_id];

                Eigen::Vector4d rel_centroid = (tok1.centroid + tok2.centroid) * 0.5;
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
                    std::vector<Eigen::Vector4d> rel_trajectory = {tok1.centroid, tok2.centroid};
                    phys_records.push_back({rel_phys_id, HilbertCurve4D::encode(hc), rel_centroid, rel_trajectory});
                }
                rel_records.push_back({rel_id, rel_phys_id});
                stats.relations_new++;

                for (uint32_t k = 0; k < 2; ++k) {
                    const auto& cid = (k == 0) ? lo : hi;
                    uint8_t rs_data[37];
                    rs_data[0] = 0x54;
                    std::memcpy(rs_data + 1, rel_id.data(), 16);
                    std::memcpy(rs_data + 17, cid.data(), 16);
                    std::memcpy(rs_data + 33, &k, 4);
                    rel_seq_records.push_back({ BLAKE3Pipeline::hash(rs_data, 37), rel_id, cid, k, 1 });
                }
            }
        }
    }

    // One evidence record per (content, relation) — aggregated
    for (const auto& [rel_id, agg] : rel_agg) {
        uint8_t ev_data[32];
        std::memcpy(ev_data, content_id.data(), 16);
        std::memcpy(ev_data + 16, rel_id.data(), 16);
        double avg_signal = agg.total_signal / static_cast<double>(agg.count);
        ev_records.push_back({ BLAKE3Pipeline::hash(ev_data, 32), content_id, rel_id, true, avg_signal, static_cast<double>(agg.count) });
        stats.evidence_count++;
    }

    // Ratings from aggregated counts
    for (const auto& [rel_id, agg] : rel_agg) {
        if (agg.count >= config_.min_cooccurrence) {
            stats.cooccurrences_significant++;
            double elo = 800.0 + 400.0 * std::log2(static_cast<double>(agg.count) + 1.0);
            rating_records.push_back({ rel_id, static_cast<uint64_t>(agg.count), elo, 32.0 });
        }
    }
    stats.cooccurrences_found = rel_agg.size();
    stats.ngrams_extracted = token_sequence.size();
    for (const auto& [id, tok] : token_map) {
        size_t cnt = 0;
        for (const auto& sid : token_sequence) if (sid == id) ++cnt;
        if (cnt >= 2) stats.ngrams_significant++;
    }
    double t_relations = ms_since(t0);

    // Phase 5: Persist — single transaction, COPY-based bulk with ON CONFLICT
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

    stats.compositions_total = token_map.size();
    stats.relations_total = rel_agg.size();
    stats.atoms_total = 0;
    for (const auto& [cp, tok] : token_map) stats.atoms_total += tok.atom_ids.size();

    double t_total_ms = ms_since(t_total);
    std::cout << "  Ingestion timing: atoms=" << std::fixed << std::setprecision(0) << t_atoms
              << "ms tokenize=" << t_tokenize << "ms compose=" << t_compose
              << "ms relations=" << t_relations << "ms persist=" << t_persist
              << "ms total=" << t_total_ms << "ms" << std::endl;

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
