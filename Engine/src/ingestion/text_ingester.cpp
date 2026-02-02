/**
 * @file text_ingester.cpp
 * @brief Text ingestion: Atoms → Compositions → Relations
 *
 * Architecture:
 * - Atoms: Unicode codepoints with POINTZM positions on S³
 * - Compositions: N-grams of ATOMS (tokens). LINESTRINGZM of atom positions → centroid
 * - Relations: N-grams of COMPOSITIONS. LINESTRINGZM of composition centroids → centroid
 * - Sequences: compositionsequence (composition→atoms), relationsequence (relation→compositions)
 */

#include <ingestion/text_ingester.hpp>
#include <storage/atom_lookup.hpp>
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
#include <unordered_set>
#include <unordered_map>
#include <vector>
#include <regex>

namespace Hartonomous {

TextIngester::TextIngester(PostgresConnection& db, const IngestionConfig& config)
    : db_(db), config_(config) {}

std::string TextIngester::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << (static_cast<unsigned>(hash[i]) & 0xFF);
    }
    return ss.str();
}

static BLAKE3Pipeline::Hash uuid_to_hash(const std::string& uuid) {
    BLAKE3Pipeline::Hash h{};
    int j = 0;
    for (size_t i = 0; i < uuid.size() && j < 16; ++i) {
        if (uuid[i] == '-') continue;
        char high = uuid[i++];
        char low = uuid[i];
        auto hex_to_int = [](char c) -> uint8_t {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        };
        h[j++] = (hex_to_int(high) << 4) | hex_to_int(low);
    }
    return h;
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

BLAKE3Pipeline::Hash TextIngester::create_content_record(const std::string& text) {
    auto content_hash = BLAKE3Pipeline::hash(text);
    std::vector<uint8_t> id_data;
    id_data.push_back(0x43); // 'C' for Content
    id_data.insert(id_data.end(), content_hash.begin(), content_hash.end());
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    return BLAKE3Pipeline::hash(id_data);
}

void TextIngester::load_global_caches() {
    std::cout << "Loading global caches..." << std::endl;
    db_.query("SELECT id FROM hartonomous.composition", [&](const std::vector<std::string>& row) {
        seen_composition_ids_.insert(uuid_to_hash(row[0]));
    });
    db_.query("SELECT id FROM hartonomous.relation", [&](const std::vector<std::string>& row) {
        seen_relation_ids_.insert(uuid_to_hash(row[0]));
    });
    std::cout << "  Loaded: " << seen_composition_ids_.size() << " compositions, "
              << seen_relation_ids_.size() << " relations." << std::endl;
}

// Token structure: a word/subword made of atoms
struct Token {
    std::u32string codepoints;           // The actual codepoints
    BLAKE3Pipeline::Hash comp_id;        // Composition hash
    BLAKE3Pipeline::Hash phys_id;        // Physicality hash
    Eigen::Vector4d centroid;            // S³ centroid position
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
    std::cout << "Ingesting " << text.size() << " bytes..." << std::endl;
    IngestionStats stats;
    stats.original_bytes = text.size();
    if (seen_composition_ids_.empty()) load_global_caches();

    // Phase 1: Get atoms from DB
    AtomLookup atom_lookup(db_);
    std::u32string utf32 = utf8_to_utf32(text);
    std::unordered_set<uint32_t> unique_cps;
    for (auto cp : utf32) unique_cps.insert(cp);
    auto atom_map = atom_lookup.lookup_batch({unique_cps.begin(), unique_cps.end()});
    std::cout << "  Found " << atom_map.size() << " atoms for " << unique_cps.size() << " unique codepoints." << std::endl;

    // Phase 2: Tokenize into words
    auto raw_tokens = tokenize(utf32);
    std::cout << "  Tokenized into " << raw_tokens.size() << " tokens." << std::endl;

    // Phase 3: Build Token objects with composition IDs and atom sequences
    std::unordered_map<BLAKE3Pipeline::Hash, Token, HashHasher> token_map;
    std::vector<BLAKE3Pipeline::Hash> token_sequence;  // Sequence of composition IDs
    token_sequence.reserve(raw_tokens.size());

    for (const auto& tok_cps : raw_tokens) {
        // Filter to only codepoints we have atoms for
        std::vector<uint32_t> valid_cps;
        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        std::vector<Eigen::Vector4d> positions;

        for (char32_t cp : tok_cps) {
            auto it = atom_map.find(cp);
            if (it != atom_map.end()) {
                valid_cps.push_back(cp);
                atom_ids.push_back(it->second.id);
                positions.push_back(it->second.position);
            }
        }

        if (valid_cps.empty()) continue;  // Skip tokens with no valid atoms

        // Composition ID = hash of atom sequence
        std::vector<uint8_t> comp_data = {0x43}; // 'C' for Composition
        for (const auto& aid : atom_ids) {
            comp_data.insert(comp_data.end(), aid.begin(), aid.end());
        }
        auto comp_id = BLAKE3Pipeline::hash(comp_data);
        token_sequence.push_back(comp_id);

        // Only process if we haven't seen this composition
        if (token_map.find(comp_id) == token_map.end()) {
            Token tok;
            tok.codepoints = tok_cps;
            tok.comp_id = comp_id;
            tok.atom_ids = atom_ids;

            // Compute centroid (average of atom positions, normalized to S³)
            Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
            for (const auto& p : positions) centroid += p;
            centroid /= static_cast<double>(positions.size());
            double norm = centroid.norm();
            if (norm > 1e-10) centroid /= norm;
            else centroid = Eigen::Vector4d(1, 0, 0, 0);
            tok.centroid = centroid;

            // Physicality ID from centroid
            std::vector<uint8_t> phys_data = {0x50}; // 'P' for Physicality
            phys_data.insert(phys_data.end(),
                reinterpret_cast<const uint8_t*>(centroid.data()),
                reinterpret_cast<const uint8_t*>(centroid.data()) + 32);
            tok.phys_id = BLAKE3Pipeline::hash(phys_data);

            token_map[comp_id] = tok;
        }
    }
    std::cout << "  Unique tokens (compositions): " << token_map.size() << std::endl;

    // Calculate N-gram statistics
    std::unordered_map<BLAKE3Pipeline::Hash, size_t, HashHasher> comp_counts;
    for (const auto& id : token_sequence) {
        comp_counts[id]++;
    }
    stats.ngrams_extracted = token_sequence.size();
    for (const auto& [id, count] : comp_counts) {
        if (count >= 2) stats.ngrams_significant++;
    }

    // Phase 4: Collect all records
    std::vector<PhysicalityRecord> phys_records;
    std::vector<CompositionRecord> comp_records;
    std::vector<CompositionSequenceRecord> seq_records;
    std::vector<RelationRecord> rel_records;
    std::vector<RelationSequenceRecord> rel_seq_records;
    std::vector<RelationEvidenceRecord> ev_records;

    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;

    // Compositions and their atom sequences
    for (auto& [comp_id, tok] : token_map) {
        if (seen_composition_ids_.insert(comp_id).second) {
            // Store physicality
            if (phys_seen.insert(tok.phys_id).second) {
                Eigen::Vector4d hc;
                for (int k = 0; k < 4; ++k) hc[k] = (tok.centroid[k] + 1.0) / 2.0;
                phys_records.push_back({tok.phys_id, HilbertCurve4D::encode(hc), tok.centroid});
            }

            // Store composition
            comp_records.push_back({comp_id, tok.phys_id});
            stats.compositions_new++;

            // Store composition sequence (composition -> atoms) with RLE
            for (size_t i = 0; i < tok.atom_ids.size(); ) {
                uint32_t ordinal = static_cast<uint32_t>(i);
                uint32_t occurrences = 1;

                // Count consecutive identical atoms (RLE)
                while (i + occurrences < tok.atom_ids.size() &&
                       tok.atom_ids[i + occurrences] == tok.atom_ids[i]) {
                    ++occurrences;
                }

                std::vector<uint8_t> seq_data = {0x53}; // 'S' for Sequence
                seq_data.insert(seq_data.end(), comp_id.begin(), comp_id.end());
                seq_data.insert(seq_data.end(), tok.atom_ids[i].begin(), tok.atom_ids[i].end());
                seq_data.insert(seq_data.end(), reinterpret_cast<uint8_t*>(&ordinal),
                               reinterpret_cast<uint8_t*>(&ordinal) + 4);
                seq_records.push_back({
                    BLAKE3Pipeline::hash(seq_data),
                    comp_id,
                    tok.atom_ids[i],
                    ordinal,
                    occurrences
                });

                i += occurrences;  // Skip over the run
            }
        }
    }

    // Phase 5: Relations (bigrams of compositions)
    auto content_id = create_content_record(text);
    std::unordered_map<BLAKE3Pipeline::Hash, size_t, HashHasher> rel_counts;

    for (size_t i = 0; i + 1 < token_sequence.size(); ++i) {
        auto& comp1_id = token_sequence[i];
        auto& comp2_id = token_sequence[i + 1];

        // Relation ID = hash of composition pair
        std::vector<uint8_t> rel_data = {0x52}; // 'R' for Relation
        rel_data.insert(rel_data.end(), comp1_id.begin(), comp1_id.end());
        rel_data.insert(rel_data.end(), comp2_id.begin(), comp2_id.end());
        auto rel_id = BLAKE3Pipeline::hash(rel_data);

        rel_counts[rel_id]++;

        if (seen_relation_ids_.insert(rel_id).second) {
            // Compute relation centroid from composition centroids
            auto& tok1 = token_map[comp1_id];
            auto& tok2 = token_map[comp2_id];
            Eigen::Vector4d rel_centroid = (tok1.centroid + tok2.centroid) * 0.5;
            double norm = rel_centroid.norm();
            if (norm > 1e-10) rel_centroid /= norm;
            else rel_centroid = Eigen::Vector4d(1, 0, 0, 0);

            // Relation physicality
            std::vector<uint8_t> rel_phys_data = {0x50};
            rel_phys_data.insert(rel_phys_data.end(),
                reinterpret_cast<const uint8_t*>(rel_centroid.data()),
                reinterpret_cast<const uint8_t*>(rel_centroid.data()) + 32);
            auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data);

            if (phys_seen.insert(rel_phys_id).second) {
                Eigen::Vector4d hc;
                for (int k = 0; k < 4; ++k) hc[k] = (rel_centroid[k] + 1.0) / 2.0;
                phys_records.push_back({rel_phys_id, HilbertCurve4D::encode(hc), rel_centroid});
            }

            rel_records.push_back({rel_id, rel_phys_id});
            stats.relations_new++;

            // Relation sequence (relation → compositions)
            for (size_t j = 0; j < 2; ++j) {
                auto& cid = (j == 0) ? comp1_id : comp2_id;
                std::vector<uint8_t> rs_data = {0x54}; // 'T' for relaTion sequence
                rs_data.insert(rs_data.end(), rel_id.begin(), rel_id.end());
                rs_data.insert(rs_data.end(), cid.begin(), cid.end());
                uint32_t ord = static_cast<uint32_t>(j);
                rs_data.insert(rs_data.end(), reinterpret_cast<uint8_t*>(&ord),
                              reinterpret_cast<uint8_t*>(&ord) + 4);
                rel_seq_records.push_back({
                    BLAKE3Pipeline::hash(rs_data),
                    rel_id,
                    cid,
                    ord,
                    1
                });
            }
        }

        // Evidence for this relation occurrence
        std::vector<uint8_t> ev_data;
        ev_data.insert(ev_data.end(), content_id.begin(), content_id.end());
        ev_data.insert(ev_data.end(), rel_id.begin(), rel_id.end());
        uint32_t pos = static_cast<uint32_t>(i);
        ev_data.insert(ev_data.end(), reinterpret_cast<uint8_t*>(&pos),
                      reinterpret_cast<uint8_t*>(&pos) + 4);
        ev_records.push_back({
            BLAKE3Pipeline::hash(ev_data),
            content_id,
            rel_id,
            true,
            1.0,  // confidence
            1.0   // weight
        });
        stats.evidence_count++;
    }

    stats.cooccurrences_found = token_sequence.size() > 1 ? token_sequence.size() - 1 : 0;
    for (const auto& [id, count] : rel_counts) {
        if (count >= 2) stats.cooccurrences_significant++;
    }

    std::cout << "  Collected: " << phys_records.size() << " phys, "
              << comp_records.size() << " comp, " << seq_records.size() << " comp_seq, "
              << rel_records.size() << " rel, " << rel_seq_records.size() << " rel_seq, "
              << ev_records.size() << " evidence" << std::endl;

    // Phase 6: Store sequentially (one COPY stream at a time)
    // Content must be stored FIRST (evidence references it)
    ContentStore(db_).store({content_id, config_.tenant_id, config_.user_id, config_.content_type,
                             content_id, stats.original_bytes, config_.mime_type, config_.language,
                             config_.source, config_.encoding});

    {
        std::cout << "  Storing physicalities..." << std::flush;
        PhysicalityStore store(db_);
        for (auto& r : phys_records) store.store(r);
        store.flush();
        std::cout << " done (" << phys_records.size() << ")." << std::endl;
    }
    {
        std::cout << "  Storing compositions..." << std::flush;
        CompositionStore store(db_);
        for (auto& r : comp_records) store.store(r);
        store.flush();
        std::cout << " done (" << comp_records.size() << ")." << std::endl;
    }
    {
        std::cout << "  Storing composition sequences..." << std::flush;
        CompositionSequenceStore store(db_);
        for (auto& r : seq_records) store.store(r);
        store.flush();
        std::cout << " done (" << seq_records.size() << ")." << std::endl;
    }
    {
        std::cout << "  Storing relations..." << std::flush;
        RelationStore store(db_);
        for (auto& r : rel_records) store.store(r);
        store.flush();
        std::cout << " done (" << rel_records.size() << ")." << std::endl;
    }
    {
        std::cout << "  Storing relation sequences..." << std::flush;
        RelationSequenceStore store(db_);
        for (auto& r : rel_seq_records) store.store(r);
        store.flush();
        std::cout << " done (" << rel_seq_records.size() << ")." << std::endl;
    }
    {
        std::cout << "  Storing evidence..." << std::flush;
        RelationEvidenceStore store(db_);
        for (auto& r : ev_records) store.store(r);
        store.flush();
        std::cout << " done (" << ev_records.size() << ")." << std::endl;
    }

    stats.compositions_total = seen_composition_ids_.size();
    stats.relations_total = seen_relation_ids_.size();
    stats.atoms_total = unique_cps.size();

    std::cout << "  Complete: " << stats.compositions_new << " new compositions, "
              << stats.relations_new << " new relations, "
              << stats.evidence_count << " evidence records." << std::endl;
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

// Stub implementations for interface compatibility
std::vector<TextIngester::Atom> TextIngester::extract_atoms(const std::u32string&) { return {}; }
std::vector<TextIngester::Composition> TextIngester::extract_compositions(const std::u32string&, const std::unordered_map<BLAKE3Pipeline::Hash, Atom, HashHasher>&) { return {}; }
std::vector<TextIngester::Relation> TextIngester::extract_relations(const std::unordered_map<BLAKE3Pipeline::Hash, Composition, HashHasher>&) { return {}; }
TextIngester::Physicality TextIngester::compute_physicality(const Vec4&) { return {}; }
void TextIngester::store_all(const BLAKE3Pipeline::Hash&, const std::vector<Atom>&, const std::vector<Composition>&, const std::vector<Relation>&, IngestionStats&) {}
BLAKE3Pipeline::Hash TextIngester::compute_sequence_hash(const std::vector<SequenceItem>&, uint8_t) { return {}; }
Vec4 TextIngester::compute_centroid(const std::vector<Vec4>&) { return {}; }
std::string TextIngester::hash_to_hex(const BLAKE3Pipeline::Hash& hash) { return BLAKE3Pipeline::to_hex(hash); }

} // namespace Hartonomous
