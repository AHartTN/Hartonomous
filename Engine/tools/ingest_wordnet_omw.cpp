// ingest_wordnet_omw.cpp
// High-performance bulk ingestion for Princeton WordNet 3.0 + OMW-data
// Architecture: Drop indexes → Parallel parse → Bulk C++ compute → Direct COPY → Rebuild indexes
// Target: Hartonomous substrate (Atoms, Compositions, Relations, Provenance)

#include <database/postgres_connection.hpp>
#include <storage/atom_lookup.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/physicality_store.hpp>
#include <storage/content_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <spatial/hilbert_curve_4d.hpp>

#include <iostream>
#include <iomanip>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <filesystem>
#include <chrono>
#include <thread>
#include <algorithm>
#include <cstring>

namespace Hartonomous {

using namespace hartonomous::spatial;
using Clock = std::chrono::steady_clock;

// ─────────────────────────────────────────────
// Data Structures
// ─────────────────────────────────────────────

struct Synset {
    std::string offset;
    char pos;
    std::vector<std::string> lemmas;
    std::string gloss;
    struct Pointer {
        std::string type;
        std::string target_offset;
        char target_pos;
    };
    std::vector<Pointer> pointers;
};

struct OMWEntry {
    std::string synset_id;
    std::string lemma;
};

struct CachedComp {
    BLAKE3Pipeline::Hash comp_id;
    BLAKE3Pipeline::Hash phys_id;
    Eigen::Vector4d centroid;
};

// ─────────────────────────────────────────────
// Composition Cache (single-threaded access only)
// ─────────────────────────────────────────────

std::unordered_map<std::string, CachedComp> g_comp_cache;
size_t g_comp_count = 0;
size_t g_rel_count = 0;

// ─────────────────────────────────────────────
// Record Batch
// ─────────────────────────────────────────────

struct RecordBatch {
    std::vector<PhysicalityRecord> phys;
    std::vector<CompositionRecord> comp;
    std::vector<CompositionSequenceRecord> seq;
    std::vector<RelationRecord> rel;
    std::vector<RelationSequenceRecord> rel_seq;
    std::vector<RelationRatingRecord> rating;
    std::vector<RelationEvidenceRecord> evidence;

    void clear() {
        phys.clear(); comp.clear(); seq.clear();
        rel.clear(); rel_seq.clear(); rating.clear(); evidence.clear();
    }
    bool empty() const { return comp.empty() && rel.empty(); }
    size_t record_count() const {
        return phys.size() + comp.size() + seq.size() +
               rel.size() + rel_seq.size() + rating.size() + evidence.size();
    }
};

// ─────────────────────────────────────────────
// Bulk Load Index Management
// ─────────────────────────────────────────────

void drop_indexes_for_bulk_load(PostgresConnection& db) {
    std::cout << "[BULK] Dropping indexes + constraints for fast load..." << std::flush;
    auto t = Clock::now();

    // Nuclear option: disable ALL FK trigger checks (same as pg_restore uses)
    db.execute("SET session_replication_role = 'replica'");

    // Drop the expensive S³ normalization CHECK (4 PostGIS calls per row)
    db.execute("ALTER TABLE hartonomous.physicality DROP CONSTRAINT IF EXISTS physicality_centroid_normalized");

    // Drop ALL non-PK indexes across all 7 tables
    // Physicality: Hilbert B-tree + 2 GIST spatial (the biggest killers)
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_physicality_hilbert");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_physicality_centroid");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_physicality_trajectory");

    // Composition: PhysicalityId + 3 timestamp indexes
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_composition_physicality");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_composition_createdat");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_composition_modifiedat");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_composition_validatedat");

    // CompositionSequence: 7 indexes + 1 unique constraint
    db.execute("DROP INDEX IF EXISTS hartonomous.uq_compositionsequence_compositionid_ordinal");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_compositionid");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_atomid");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_ordinal");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_occurrences");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_createdat");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_modifiedat");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_validatedat");

    // Relation: PhysicalityId
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relation_physicality");

    // RelationSequence: unique + 5 indexes
    db.execute("DROP INDEX IF EXISTS hartonomous.uq_relationsequence_relationid_ordinal");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relationsequence_relationid");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relationsequence_compositionid");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relationsequence_createdat");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relationsequence_modifiedat");
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relationsequence_validatedat");

    // RelationRating
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relationrating_ratingvalue");

    // RelationEvidence
    db.execute("DROP INDEX IF EXISTS hartonomous.idx_relationevidence_sourcerating");

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - t).count();
    std::cout << " done (" << ms << "ms, ~25 indexes dropped)" << std::endl;
}

void rebuild_indexes_after_bulk_load(PostgresConnection& db) {
    std::cout << "[BULK] Rebuilding indexes (bulk-built, much faster than incremental)..." << std::endl;
    auto t = Clock::now();

    auto timed = [&](const char* label, const char* sql) {
        auto s = Clock::now();
        db.execute(sql);
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - s).count();
        std::cout << "  " << label << " (" << ms << "ms)" << std::endl;
    };

    // Physicality
    timed("Physicality: Hilbert",
        "CREATE INDEX idx_physicality_hilbert ON hartonomous.physicality(hilbert)");
    timed("Physicality: Centroid GIST",
        "CREATE INDEX idx_physicality_centroid ON hartonomous.physicality USING GIST(centroid gist_geometry_ops_nd)");
    timed("Physicality: Trajectory GIST",
        "CREATE INDEX idx_physicality_trajectory ON hartonomous.physicality USING GIST(trajectory gist_geometry_ops_nd)");
    timed("Physicality: S3 CHECK",
        "ALTER TABLE hartonomous.physicality ADD CONSTRAINT physicality_centroid_normalized "
        "CHECK (ABS(ST_X(centroid)*ST_X(centroid) + ST_Y(centroid)*ST_Y(centroid) + "
        "ST_Z(centroid)*ST_Z(centroid) + ST_M(centroid)*ST_M(centroid) - 1.0) < 0.0001) NOT VALID");

    // Composition
    timed("Composition: Physicality",
        "CREATE INDEX idx_composition_physicality ON hartonomous.composition(physicalityid)");
    timed("Composition: CreatedAt",
        "CREATE INDEX idx_composition_createdat ON hartonomous.composition(createdat)");
    timed("Composition: ModifiedAt",
        "CREATE INDEX idx_composition_modifiedat ON hartonomous.composition(modifiedat)");
    timed("Composition: ValidatedAt",
        "CREATE INDEX idx_composition_validatedat ON hartonomous.composition(validatedat)");

    // CompositionSequence
    timed("CompSeq: UNIQUE(CompositionId,Ordinal)",
        "CREATE UNIQUE INDEX uq_compositionsequence_compositionid_ordinal ON hartonomous.compositionsequence(compositionid, ordinal)");
    timed("CompSeq: CompositionId",
        "CREATE INDEX idx_compositionsequence_compositionid ON hartonomous.compositionsequence(compositionid)");
    timed("CompSeq: AtomId",
        "CREATE INDEX idx_compositionsequence_atomid ON hartonomous.compositionsequence(atomid)");
    timed("CompSeq: Ordinal",
        "CREATE INDEX idx_compositionsequence_ordinal ON hartonomous.compositionsequence(ordinal)");
    timed("CompSeq: Occurrences",
        "CREATE INDEX idx_compositionsequence_occurrences ON hartonomous.compositionsequence(occurrences)");
    timed("CompSeq: CreatedAt",
        "CREATE INDEX idx_compositionsequence_createdat ON hartonomous.compositionsequence(createdat)");
    timed("CompSeq: ModifiedAt",
        "CREATE INDEX idx_compositionsequence_modifiedat ON hartonomous.compositionsequence(modifiedat)");
    timed("CompSeq: ValidatedAt",
        "CREATE INDEX idx_compositionsequence_validatedat ON hartonomous.compositionsequence(validatedat)");

    // Relation
    timed("Relation: Physicality",
        "CREATE INDEX idx_relation_physicality ON hartonomous.relation(physicalityid)");

    // RelationSequence
    timed("RelSeq: UNIQUE(RelationId,Ordinal)",
        "CREATE UNIQUE INDEX uq_relationsequence_relationid_ordinal ON hartonomous.relationsequence(relationid, ordinal)");
    timed("RelSeq: RelationId",
        "CREATE INDEX idx_relationsequence_relationid ON hartonomous.relationsequence(relationid, ordinal ASC, occurrences)");
    timed("RelSeq: CompositionId",
        "CREATE INDEX idx_relationsequence_compositionid ON hartonomous.relationsequence(compositionid, relationid)");
    timed("RelSeq: CreatedAt",
        "CREATE INDEX idx_relationsequence_createdat ON hartonomous.relationsequence(createdat)");
    timed("RelSeq: ModifiedAt",
        "CREATE INDEX idx_relationsequence_modifiedat ON hartonomous.relationsequence(modifiedat)");
    timed("RelSeq: ValidatedAt",
        "CREATE INDEX idx_relationsequence_validatedat ON hartonomous.relationsequence(validatedat)");

    // RelationRating
    timed("RelRating: RatingValue",
        "CREATE INDEX idx_relationrating_ratingvalue ON hartonomous.relationrating(ratingvalue)");

    // RelationEvidence
    timed("RelEvidence: SourceRating",
        "CREATE INDEX idx_relationevidence_sourcerating ON hartonomous.relationevidence(sourcerating)");

    // Re-enable FK constraints
    db.execute("SET session_replication_role = 'origin'");

    auto total_ms = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - t).count();
    std::cout << "[BULK] All indexes rebuilt (" << total_ms << "ms)" << std::endl;
}

// ─────────────────────────────────────────────
// DB Flush - Direct COPY (no temp tables)
// ─────────────────────────────────────────────

bool flush_batch(PostgresConnection& db, RecordBatch& batch) {
    if (batch.empty()) return true;

    try {
        // Direct COPY: no temp table overhead, no ON CONFLICT.
        // C++ cache guarantees dedup. FK checks disabled via session_replication_role.
        PostgresConnection::Transaction txn(db);
        { PhysicalityStore s(db, false, true); for (auto& r : batch.phys) s.store(r); s.flush(); }
        { CompositionStore s(db, false, true); for (auto& r : batch.comp) s.store(r); s.flush(); }
        { CompositionSequenceStore s(db, false, true); for (auto& r : batch.seq) s.store(r); s.flush(); }
        { RelationStore s(db, false, true); for (auto& r : batch.rel) s.store(r); s.flush(); }
        { RelationSequenceStore s(db, false, true); for (auto& r : batch.rel_seq) s.store(r); s.flush(); }
        { RelationRatingStore s(db, true); for (auto& r : batch.rating) s.store(r); s.flush(); }
        { RelationEvidenceStore s(db, false, true); for (auto& r : batch.evidence) s.store(r); s.flush(); }
        txn.commit();
        batch.clear();
        return true;
    } catch (const std::exception& e) {
        std::cerr << "[ERROR] Batch flush failed: " << e.what() << std::endl;
        batch.clear();
        return false;
    }
}

// ─────────────────────────────────────────────
// UTF-8 → UTF-32
// ─────────────────────────────────────────────

std::u32string utf8_to_utf32(const std::string& s) {
    std::u32string out;
    out.reserve(s.size());
    size_t i = 0;
    while (i < s.size()) {
        uint8_t c = s[i];
        char32_t cp = 0;
        size_t len = 0;
        if (c < 0x80)              { cp = c;          len = 1; }
        else if ((c >> 5) == 0x6)  { cp = c & 0x1F;   len = 2; }
        else if ((c >> 4) == 0xE)  { cp = c & 0x0F;   len = 3; }
        else if ((c >> 3) == 0x1E) { cp = c & 0x07;   len = 4; }
        else                       { ++i; continue; }
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

// ─────────────────────────────────────────────
// Composition Creation (all C++ compute)
// ─────────────────────────────────────────────

CachedComp get_or_create_comp(const std::string& text, AtomLookup& lookup, RecordBatch& batch) {
    auto it = g_comp_cache.find(text);
    if (it != g_comp_cache.end()) return it->second;

    std::u32string utf32 = utf8_to_utf32(text);
    if (utf32.empty()) return {};

    std::vector<BLAKE3Pipeline::Hash> atom_ids;
    std::vector<Eigen::Vector4d> positions;
    atom_ids.reserve(utf32.size());
    positions.reserve(utf32.size());

    for (char32_t cp : utf32) {
        auto info = lookup.lookup(cp);
        if (info) {
            atom_ids.push_back(info->id);
            positions.push_back(info->position);
        }
    }
    if (atom_ids.empty()) return {};

    // Composition ID
    size_t comp_data_len = 1 + atom_ids.size() * 16;
    std::vector<uint8_t> comp_data(comp_data_len);
    comp_data[0] = 0x43;
    for (size_t k = 0; k < atom_ids.size(); ++k)
        std::memcpy(comp_data.data() + 1 + k * 16, atom_ids[k].data(), 16);
    auto comp_id = BLAKE3Pipeline::hash(comp_data.data(), comp_data_len);

    // Centroid (Eigen AVX2)
    Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
    for (const auto& p : positions) centroid += p;
    centroid /= static_cast<double>(positions.size());
    double norm = centroid.norm();
    if (norm > 1e-10) centroid /= norm;
    else centroid = Eigen::Vector4d(1, 0, 0, 0);

    // Physicality ID
    uint8_t phys_data[33];
    phys_data[0] = 0x50;
    std::memcpy(phys_data + 1, centroid.data(), sizeof(double) * 4);
    auto phys_id = BLAKE3Pipeline::hash(phys_data, 33);

    Eigen::Vector4d hc;
    for (int k = 0; k < 4; ++k) hc[k] = (centroid[k] + 1.0) / 2.0;

    batch.phys.push_back({phys_id, HilbertCurve4D::encode(hc), centroid, positions});
    batch.comp.push_back({comp_id, phys_id});

    // Composition sequence with RLE
    for (size_t i = 0; i < atom_ids.size(); ) {
        uint32_t ordinal = static_cast<uint32_t>(i);
        uint32_t occurrences = 1;
        while (i + occurrences < atom_ids.size() && atom_ids[i + occurrences] == atom_ids[i])
            ++occurrences;

        uint8_t seq_data[37];
        seq_data[0] = 0x53;
        std::memcpy(seq_data + 1, comp_id.data(), 16);
        std::memcpy(seq_data + 17, atom_ids[i].data(), 16);
        std::memcpy(seq_data + 33, &ordinal, 4);

        batch.seq.push_back({BLAKE3Pipeline::hash(seq_data, 37), comp_id, atom_ids[i], ordinal, occurrences});
        i += occurrences;
    }

    CachedComp res = {comp_id, phys_id, centroid};
    g_comp_cache[text] = res;
    g_comp_count++;
    return res;
}

// ─────────────────────────────────────────────
// Relation Creation (all C++ compute)
// ─────────────────────────────────────────────

void queue_relation(const CachedComp& a, const CachedComp& b,
                    const BLAKE3Pipeline::Hash& content_id, RecordBatch& batch,
                    double rating = 1500.0) {

    uint8_t rel_input[33];
    rel_input[0] = 0x52;
    std::memcpy(rel_input + 1, a.comp_id.data(), 16);
    std::memcpy(rel_input + 17, b.comp_id.data(), 16);
    auto rel_id = BLAKE3Pipeline::hash(rel_input, 33);

    Eigen::Vector4d rel_centroid = (a.centroid + b.centroid) * 0.5;
    double norm = rel_centroid.norm();
    if (norm > 1e-10) rel_centroid /= norm;
    else rel_centroid = Eigen::Vector4d(1, 0, 0, 0);

    uint8_t rel_phys_data[33];
    rel_phys_data[0] = 0x50;
    std::memcpy(rel_phys_data + 1, rel_centroid.data(), sizeof(double) * 4);
    auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data, 33);

    Eigen::Vector4d hc;
    for (int k = 0; k < 4; ++k) hc[k] = (rel_centroid[k] + 1.0) / 2.0;
    std::vector<Eigen::Vector4d> rel_trajectory = {a.centroid, b.centroid};

    batch.phys.push_back({rel_phys_id, HilbertCurve4D::encode(hc), rel_centroid, rel_trajectory});
    batch.rel.push_back({rel_id, rel_phys_id});

    for (uint32_t k = 0; k < 2; ++k) {
        const auto& cid = (k == 0) ? a.comp_id : b.comp_id;
        uint8_t rs_data[37];
        rs_data[0] = 0x54;
        std::memcpy(rs_data + 1, rel_id.data(), 16);
        std::memcpy(rs_data + 17, cid.data(), 16);
        std::memcpy(rs_data + 33, &k, 4);
        batch.rel_seq.push_back({BLAKE3Pipeline::hash(rs_data, 37), rel_id, cid, k, 1});
    }

    uint8_t ev_data[32];
    std::memcpy(ev_data, content_id.data(), 16);
    std::memcpy(ev_data + 16, rel_id.data(), 16);
    batch.evidence.push_back({BLAKE3Pipeline::hash(ev_data, 32), content_id, rel_id, true, rating, 1.0});
    batch.rating.push_back({rel_id, 1, rating, 32.0});
    g_rel_count++;
}

// ─────────────────────────────────────────────
// WordNet Parser
// ─────────────────────────────────────────────

void parse_wordnet_file(const std::string& file, char expected_pos, std::vector<Synset>& synsets) {
    std::ifstream in(file);
    if (!in) { std::cerr << "[WARN] Cannot open: " << file << std::endl; return; }
    std::string line;
    while (std::getline(in, line)) {
        if (line.empty() || line[0] == ' ' || line[0] == '#') continue;
        size_t pipe_pos = line.find('|');
        std::string synset_part = (pipe_pos == std::string::npos) ? line : line.substr(0, pipe_pos);
        std::string gloss = (pipe_pos == std::string::npos) ? "" : line.substr(pipe_pos + 1);

        std::istringstream iss(synset_part);
        std::string offset, lex_filenum, word_count_hex;
        char pos;
        if (!(iss >> offset >> lex_filenum >> pos >> word_count_hex)) continue;
        if (pos != expected_pos && !(pos == 's' && expected_pos == 'a')) continue;

        int word_count = std::stoi(word_count_hex, nullptr, 16);
        std::vector<std::string> lemmas;
        lemmas.reserve(word_count);
        for (int i = 0; i < word_count; ++i) {
            std::string lemma, lex_id_hex;
            iss >> lemma >> lex_id_hex;
            lemmas.push_back(std::move(lemma));
        }

        std::string pointer_count_str;
        if (!(iss >> pointer_count_str)) continue;
        int pointer_count = std::stoi(pointer_count_str, nullptr, 10);
        std::vector<Synset::Pointer> pointers;
        pointers.reserve(pointer_count);
        for (int i = 0; i < pointer_count; ++i) {
            std::string sym, target_offset, source_target_sense;
            char target_pos;
            if (!(iss >> sym >> target_offset >> target_pos >> source_target_sense)) break;
            pointers.push_back({std::move(sym), std::move(target_offset), target_pos});
        }

        if (!gloss.empty()) {
            size_t start = gloss.find_first_not_of(" \t\r\n");
            size_t end = gloss.find_last_not_of(" \t\r\n");
            if (start != std::string::npos) gloss = gloss.substr(start, end - start + 1);
            else gloss.clear();
        }
        synsets.push_back({std::move(offset), pos, std::move(lemmas), std::move(gloss), std::move(pointers)});
    }
}

// ─────────────────────────────────────────────
// OMW Parser (parallel I/O)
// ─────────────────────────────────────────────

void parse_omw_files_parallel(const std::vector<std::string>& files, std::vector<OMWEntry>& entries) {
    size_t num_threads = std::min(static_cast<size_t>(std::thread::hardware_concurrency()), files.size());
    if (num_threads == 0) num_threads = 1;

    std::vector<std::vector<std::string>> chunks(num_threads);
    for (size_t i = 0; i < files.size(); ++i) chunks[i % num_threads].push_back(files[i]);

    std::vector<std::vector<OMWEntry>> thread_results(num_threads);
    std::vector<std::thread> workers;
    workers.reserve(num_threads);

    for (size_t t = 0; t < num_threads; ++t) {
        workers.emplace_back([&chunks, &thread_results, t]() {
            auto& local = thread_results[t];
            local.reserve(50000);
            for (const auto& path : chunks[t]) {
                std::ifstream in(path);
                if (!in) continue;
                std::string line;
                while (std::getline(in, line)) {
                    if (line.empty() || line[0] == '#') continue;
                    size_t tab1 = line.find('\t');
                    if (tab1 == std::string::npos) continue;
                    size_t tab2 = line.find('\t', tab1 + 1);
                    if (tab2 == std::string::npos) continue;

                    std::string synset_raw = line.substr(0, tab1);
                    std::string lemma = line.substr(tab2 + 1);
                    size_t end = lemma.find_last_not_of("\t\r\n ");
                    if (end != std::string::npos) lemma.resize(end + 1);
                    if (lemma.empty()) continue;

                    std::string synset_id = synset_raw;
                    size_t last_dash = synset_id.find_last_of('-');
                    if (last_dash != std::string::npos && last_dash > 8) {
                        size_t prev_dash = synset_id.find_last_of('-', last_dash - 1);
                        if (prev_dash != std::string::npos) synset_id = synset_id.substr(prev_dash + 1);
                    }
                    local.push_back({std::move(synset_id), std::move(lemma)});
                }
            }
        });
    }
    for (auto& w : workers) w.join();

    size_t total = 0;
    for (const auto& r : thread_results) total += r.size();
    entries.reserve(total);
    for (auto& r : thread_results)
        entries.insert(entries.end(), std::make_move_iterator(r.begin()), std::make_move_iterator(r.end()));
}

// ─────────────────────────────────────────────
// Timer helper
// ─────────────────────────────────────────────

double elapsed_ms(Clock::time_point start) {
    return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - start).count();
}

} // namespace Hartonomous

// =============================================================================
// MAIN
// =============================================================================

int main(int argc, char** argv) {
    if (argc < 3) {
        std::cerr << "Usage: " << argv[0] << " <wordnet_dict_dir> <omw_data_dir>" << std::endl;
        return 1;
    }

    using namespace Hartonomous;
    std::string wordnet_dir = argv[1];
    std::string omw_data_dir = argv[2];
    auto total_start = Clock::now();

    // Huge batches: flush only at phase boundaries or every 500k records
    static constexpr size_t BATCH_THRESHOLD = 500000;

    try {
        PostgresConnection db;
        db.execute("SET synchronous_commit = off");
        db.execute("SET work_mem = '256MB'");
        db.execute("SET maintenance_work_mem = '1GB'");

        // ─── Phase 0: Load atom cache ───
        AtomLookup lookup(db);
        std::cout << "[Phase 0] Preloading 1.1M atoms..." << std::flush;
        auto t0 = Clock::now();
        lookup.preload_all();
        std::cout << " (" << elapsed_ms(t0) << "ms)" << std::endl;

        // ─── Content provenance ───
        BLAKE3Pipeline::Hash wn_content_id = BLAKE3Pipeline::hash("source:wordnet-3.0");
        BLAKE3Pipeline::Hash omw_content_id = BLAKE3Pipeline::hash("source:omw-data");
        {
            ContentStore cs(db, false, false);
            cs.store({wn_content_id, BLAKE3Pipeline::hash("tenant:system"), BLAKE3Pipeline::hash("user:curator"),
                      2, BLAKE3Pipeline::hash("wordnet-3.0-weights"), 0,
                      "application/x-wordnet", "eng", "Princeton WordNet 3.0", "ascii"});
            cs.store({omw_content_id, BLAKE3Pipeline::hash("tenant:system"), BLAKE3Pipeline::hash("user:curator"),
                      2, BLAKE3Pipeline::hash("omw-data-weights"), 0,
                      "application/x-omw", "multi", "Open Multilingual WordNet", "utf-8"});
            cs.flush();
        }

        // ═══════════════════════════════════════════════════════════
        // DROP INDEXES (the #1 bulk-load optimization)
        // ═══════════════════════════════════════════════════════════
        drop_indexes_for_bulk_load(db);

        // ═══════════════════════════════════════════════════════════
        // Phase 1: Parse WordNet
        // ═══════════════════════════════════════════════════════════
        std::cout << "[Phase 1] Parsing WordNet..." << std::flush;
        auto t1 = Clock::now();
        std::vector<Synset> synsets;
        synsets.reserve(120000);
        parse_wordnet_file(wordnet_dir + "/data.noun", 'n', synsets);
        parse_wordnet_file(wordnet_dir + "/data.verb", 'v', synsets);
        parse_wordnet_file(wordnet_dir + "/data.adj",  'a', synsets);
        parse_wordnet_file(wordnet_dir + "/data.adv",  'r', synsets);
        std::cout << " " << synsets.size() << " synsets (" << elapsed_ms(t1) << "ms)" << std::endl;

        // ═══════════════════════════════════════════════════════════
        // Phase 2: WordNet compositions + lemma relations
        // ═══════════════════════════════════════════════════════════
        std::cout << "[Phase 2] Building compositions + lemma relations..." << std::endl;
        auto t2 = Clock::now();

        std::unordered_map<std::string, CachedComp> synset_comps;
        synset_comps.reserve(synsets.size());
        RecordBatch batch;

        for (size_t si = 0; si < synsets.size(); ++si) {
            const auto& syn = synsets[si];
            std::string key = syn.offset + "-" + static_cast<char>(syn.pos == 's' ? 'a' : syn.pos);

            CachedComp s_comp = get_or_create_comp(key, lookup, batch);
            if (s_comp.comp_id == BLAKE3Pipeline::Hash{}) continue;
            synset_comps[key] = s_comp;

            if (!syn.gloss.empty()) {
                CachedComp g_comp = get_or_create_comp(syn.gloss, lookup, batch);
                if (g_comp.comp_id != BLAKE3Pipeline::Hash{})
                    queue_relation(s_comp, g_comp, wn_content_id, batch, 1800.0);
            }
            for (const auto& lemma : syn.lemmas) {
                CachedComp l_comp = get_or_create_comp(lemma, lookup, batch);
                if (l_comp.comp_id != BLAKE3Pipeline::Hash{})
                    queue_relation(l_comp, s_comp, wn_content_id, batch, 1900.0);
            }

            if (batch.record_count() > BATCH_THRESHOLD) {
                flush_batch(db, batch);
                std::cout << "  [Phase 2] Flushed at " << (si+1) << "/" << synsets.size()
                          << " (" << g_comp_count << " comps, " << g_rel_count << " rels)" << std::endl;
            }
        }
        flush_batch(db, batch);
        std::cout << "  Phase 2 done: " << g_comp_count << " comps, " << g_rel_count
                  << " rels (" << elapsed_ms(t2) << "ms)" << std::endl;

        // ═══════════════════════════════════════════════════════════
        // Phase 3: WordNet semantic pointers
        // ═══════════════════════════════════════════════════════════
        std::cout << "[Phase 3] Linking semantic relations..." << std::flush;
        auto t3 = Clock::now();
        size_t pointer_count = 0;

        for (const auto& syn : synsets) {
            std::string key = syn.offset + "-" + static_cast<char>(syn.pos == 's' ? 'a' : syn.pos);
            auto sit = synset_comps.find(key);
            if (sit == synset_comps.end()) continue;
            for (const auto& ptr : syn.pointers) {
                std::string tkey = ptr.target_offset + "-" +
                    static_cast<char>(ptr.target_pos == 's' ? 'a' : ptr.target_pos);
                auto tit = synset_comps.find(tkey);
                if (tit != synset_comps.end()) {
                    queue_relation(sit->second, tit->second, wn_content_id, batch, 1700.0);
                    pointer_count++;
                }
            }
            if (batch.record_count() > BATCH_THRESHOLD) flush_batch(db, batch);
        }
        flush_batch(db, batch);
        std::cout << " " << pointer_count << " pointers (" << elapsed_ms(t3) << "ms)" << std::endl;

        // ═══════════════════════════════════════════════════════════
        // Phase 4: Parse OMW files (parallel I/O)
        // ═══════════════════════════════════════════════════════════
        std::vector<std::string> omw_files;
        for (const auto& entry : std::filesystem::recursive_directory_iterator(omw_data_dir)) {
            if (entry.is_regular_file() && entry.path().extension() == ".tab" &&
                entry.path().filename().string().rfind("wn-data-", 0) == 0)
                omw_files.push_back(entry.path().string());
        }

        std::cout << "[Phase 4] Parsing " << omw_files.size() << " OMW files..." << std::flush;
        auto t4 = Clock::now();
        std::vector<OMWEntry> omw_entries;
        parse_omw_files_parallel(omw_files, omw_entries);
        std::cout << " " << omw_entries.size() << " entries (" << elapsed_ms(t4) << "ms)" << std::endl;

        // ═══════════════════════════════════════════════════════════
        // Phase 5: Ingest OMW (single-connection, direct COPY)
        // ═══════════════════════════════════════════════════════════
        std::cout << "[Phase 5] Ingesting OMW..." << std::endl;
        auto t5 = Clock::now();
        size_t omw_matched = 0, omw_skipped = 0;

        for (size_t i = 0; i < omw_entries.size(); ++i) {
            const auto& entry = omw_entries[i];
            auto it = synset_comps.find(entry.synset_id);
            if (it == synset_comps.end()) { omw_skipped++; continue; }

            CachedComp l_comp = get_or_create_comp(entry.lemma, lookup, batch);
            if (l_comp.comp_id != BLAKE3Pipeline::Hash{}) {
                queue_relation(l_comp, it->second, omw_content_id, batch, 1600.0);
                omw_matched++;
            }

            if (batch.record_count() > BATCH_THRESHOLD) {
                flush_batch(db, batch);
                std::cout << "  [Phase 5] " << (i+1) << "/" << omw_entries.size()
                          << " (" << omw_matched << " matched)" << std::endl;
            }
        }
        flush_batch(db, batch);
        std::cout << "  OMW: " << omw_matched << " matched, " << omw_skipped
                  << " skipped (" << elapsed_ms(t5) << "ms)" << std::endl;

        // ═══════════════════════════════════════════════════════════
        // REBUILD INDEXES (bulk-built from sorted data = fast)
        // ═══════════════════════════════════════════════════════════
        rebuild_indexes_after_bulk_load(db);

        // ═══════════════════════════════════════════════════════════
        // Summary
        // ═══════════════════════════════════════════════════════════
        double total_sec = elapsed_ms(total_start) / 1000.0;
        std::cout << "\n[SUCCESS] WordNet + OMW Ingestion Complete." << std::endl;
        std::cout << "  Compositions: " << g_comp_count << std::endl;
        std::cout << "  Relations:    " << g_rel_count << std::endl;
        std::cout << "  Synsets:      " << synset_comps.size() << std::endl;
        std::cout << "  OMW linked:   " << omw_matched << std::endl;
        std::cout << "  Total time:   " << std::fixed << std::setprecision(1) << total_sec << "s" << std::endl;
        if (total_sec > 0)
            std::cout << "  Throughput:   " << std::fixed << std::setprecision(0)
                      << (g_rel_count / total_sec) << " relations/sec" << std::endl;

    } catch (const std::exception& ex) {
        std::cerr << "[FATAL] " << ex.what() << std::endl;
        return 1;
    }
    return 0;
}
