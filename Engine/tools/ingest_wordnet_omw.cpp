// ingest_wordnet_omw.cpp
// Integrated, Parallel ingestion tool for Princeton WordNet 3.0 + OMW-data
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
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <set>
#include <map>
#include <filesystem>
#include <chrono>
#include <thread>
#include <algorithm>
#include <mutex>
#include <atomic>

namespace Hartonomous {

using namespace hartonomous::spatial;

// -----------------------------
// Data Structures
// -----------------------------

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

struct CachedComp {
    BLAKE3Pipeline::Hash comp_id;
    BLAKE3Pipeline::Hash phys_id;
    Eigen::Vector4d centroid;
};

// -----------------------------
// Global State
// -----------------------------

std::mutex g_cache_mtx;
std::unordered_map<std::string, CachedComp> g_comp_cache; 

std::atomic<size_t> g_comp_count{0};
std::atomic<size_t> g_rel_count{0};

// -----------------------------
// Batch Management
// -----------------------------

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
};

void flush_batch(PostgresConnection& db, RecordBatch& batch) {
    if (batch.empty()) return;

    // We MUST use separate scopes or sequential flushes because only one COPY 
    // can be active on a single connection at a time.
    try {
        PostgresConnection::Transaction txn(db);
        { PhysicalityStore s(db, true, true); for (auto& r : batch.phys) s.store(r); s.flush(); }
        { CompositionStore s(db, true, true); for (auto& r : batch.comp) s.store(r); s.flush(); }
        { CompositionSequenceStore s(db, true, true); for (auto& r : batch.seq) s.store(r); s.flush(); }
        { RelationStore s(db, true, true); for (auto& r : batch.rel) s.store(r); s.flush(); }
        { RelationSequenceStore s(db, true, true); for (auto& r : batch.rel_seq) s.store(r); s.flush(); }
        { RelationRatingStore s(db, true); for (auto& r : batch.rating) s.store(r); s.flush(); }
        { RelationEvidenceStore s(db, true, true); for (auto& r : batch.evidence) s.store(r); s.flush(); }
        txn.commit();
    } catch (const std::exception& e) {
        std::cerr << "[ERROR] Batch commit failed: " << e.what() << std::endl;
    }
    batch.clear();
}

// -----------------------------
// Substrate Breakdown Logic
// -----------------------------

std::u32string utf8_to_utf32(const std::string& s) {
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
        else { ++i; continue; } 
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

CachedComp get_or_create_comp(const std::string& text, AtomLookup& lookup, RecordBatch& batch) {
    {
        std::lock_guard<std::mutex> lock(g_cache_mtx);
        auto it = g_comp_cache.find(text);
        if (it != g_comp_cache.end()) return it->second;
    }

    std::u32string utf32 = utf8_to_utf32(text);
    if (utf32.empty()) return {};

    std::vector<BLAKE3Pipeline::Hash> atom_ids;
    std::vector<Eigen::Vector4d> positions;

    for (char32_t cp : utf32) {
        auto info = lookup.lookup(cp);
        if (info) {
            atom_ids.push_back(info->id);
            positions.push_back(info->position);
        }
    }

    if (atom_ids.empty()) return {};

    // 1. Composition ID = BLAKE3(0x43 + atom_id sequence)
    size_t comp_data_len = 1 + atom_ids.size() * 16;
    std::vector<uint8_t> comp_data(comp_data_len);
    comp_data[0] = 0x43; 
    for (size_t k = 0; k < atom_ids.size(); ++k)
        std::memcpy(comp_data.data() + 1 + k * 16, atom_ids[k].data(), 16);
    auto comp_id = BLAKE3Pipeline::hash(comp_data.data(), comp_data_len);

    // 2. Physicality
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

    Eigen::Vector4d hc;
    for (int k = 0; k < 4; ++k) hc[k] = (centroid[k] + 1.0) / 2.0;
    
    // 3. Queue Records
    batch.phys.push_back({phys_id, HilbertCurve4D::encode(hc), centroid, positions});
    batch.comp.push_back({comp_id, phys_id});
    
    for (size_t i = 0; i < atom_ids.size(); ) {
        uint32_t ordinal = static_cast<uint32_t>(i);
        uint32_t occurrences = 1;
        while (i + occurrences < atom_ids.size() && atom_ids[i + occurrences] == atom_ids[i]) ++occurrences;
        
        uint8_t seq_data[37];
        seq_data[0] = 0x53; 
        std::memcpy(seq_data + 1, comp_id.data(), 16);
        std::memcpy(seq_data + 17, atom_ids[i].data(), 16);
        std::memcpy(seq_data + 33, &ordinal, 4);
        
        batch.seq.push_back({ BLAKE3Pipeline::hash(seq_data, 37), comp_id, atom_ids[i], ordinal, occurrences });
        i += occurrences;
    }

    CachedComp res = {comp_id, phys_id, centroid};
    {
        std::lock_guard<std::mutex> lock(g_cache_mtx);
        g_comp_cache[text] = res;
    }
    g_comp_count++;
    return res;
}

void queue_relation(const CachedComp& a, const CachedComp& b, const std::string& type, 
                     const BLAKE3Pipeline::Hash& content_id, RecordBatch& batch, double rating = 1500.0) {
    
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
        batch.rel_seq.push_back({ BLAKE3Pipeline::hash(rs_data, 37), rel_id, cid, k, 1 });
    }

    uint8_t ev_data[32];
    std::memcpy(ev_data, content_id.data(), 16);
    std::memcpy(ev_data + 16, rel_id.data(), 16);
    batch.evidence.push_back({ BLAKE3Pipeline::hash(ev_data, 32), content_id, rel_id, true, rating, 1.0 });
    batch.rating.push_back({ rel_id, 1, rating, 32.0 });
    g_rel_count++;
}

// -----------------------------
// Parsing Logic
// -----------------------------

void parse_wordnet_file(const std::string& file, char expected_pos, std::vector<Synset>& synsets) {
    std::ifstream in(file);
    if (!in) return;
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
        for (int i = 0; i < word_count; ++i) {
            std::string lemma, lex_id_hex;
            iss >> lemma >> lex_id_hex;
            lemmas.push_back(lemma);
        }
        std::string pointer_count_str;
        if (!(iss >> pointer_count_str)) continue;
        int pointer_count = std::stoi(pointer_count_str, nullptr, 10);
        std::vector<Synset::Pointer> pointers;
        for (int i = 0; i < pointer_count; ++i) {
            std::string sym, target_offset, source_target_sense;
            char target_pos;
            if (!(iss >> sym >> target_offset >> target_pos >> source_target_sense)) break;
            pointers.push_back({sym, target_offset, target_pos});
        }
        synsets.push_back({offset, pos, std::move(lemmas), gloss, std::move(pointers)});
    }
}

// -----------------------------
// Workers
// -----------------------------

void omw_worker(const std::vector<std::string>& file_chunk, AtomLookup& lookup, 
                const std::unordered_map<std::string, CachedComp>& synset_comps,
                BLAKE3Pipeline::Hash content_id) {
    PostgresConnection db;
    db.execute("SET synchronous_commit = off");
    RecordBatch batch;
    size_t total_processed = 0;

    for (const auto& path : file_chunk) {
        std::ifstream in(path);
        std::string line;
        while (std::getline(in, line)) {
            if (line.empty() || line[0] == '#') continue;
            std::stringstream ss(line);
            std::string synset_raw, tag, lemma;
            if (!std::getline(ss, synset_raw, '\t')) continue;
            if (!std::getline(ss, tag, '\t')) continue;
            if (!std::getline(ss, lemma, '\t')) continue;

            std::string synset_id = synset_raw;
            size_t last_dash = synset_id.find_last_of('-');
            if (last_dash != std::string::npos && last_dash > 8) {
                size_t prev_dash = synset_id.find_last_of('-', last_dash - 1);
                if (prev_dash != std::string::npos) synset_id = synset_id.substr(prev_dash + 1);
            }

            auto it = synset_comps.find(synset_id);
            if (it != synset_comps.end()) {
                CachedComp l_comp = get_or_create_comp(lemma, lookup, batch);
                if (l_comp.comp_id != BLAKE3Pipeline::Hash{}) {
                    queue_relation(l_comp, it->second, "lemma_of", content_id, batch, 1600.0);
                }
            }

            if (++total_processed % 10000 == 0) {
                flush_batch(db, batch);
            }
        }
    }
    flush_batch(db, batch);
}

} // namespace Hartonomous

int main(int argc, char** argv) {
    if (argc < 3) {
        std::cerr << "Usage: " << argv[0] << " <wordnet_dict_dir> <omw_data_dir>" << std::endl;
        return 1;
    }

    using namespace Hartonomous;
    std::string wordnet_dir = argv[1];
    std::string omw_data_dir = argv[2];

    try {
        PostgresConnection db;
        AtomLookup lookup(db);
        std::cout << "[INFO] Preloading 1.1M atoms into memory..." << std::endl;
        lookup.preload_all();

        // 1. Content Metadata
        BLAKE3Pipeline::Hash wn_content_id = BLAKE3Pipeline::hash("source:wordnet-3.0");
        BLAKE3Pipeline::Hash omw_content_id = BLAKE3Pipeline::hash("source:omw-data");
        {
            ContentStore cs(db, false, false); 
            cs.store({wn_content_id, BLAKE3Pipeline::hash("tenant:system"), BLAKE3Pipeline::hash("user:curator"), 
                      2, BLAKE3Pipeline::hash("wordnet-3.0-weights"), 0, "application/x-wordnet", "eng", "Princeton WordNet 3.0", "ascii"});
            cs.store({omw_content_id, BLAKE3Pipeline::hash("tenant:system"), BLAKE3Pipeline::hash("user:curator"), 
                      2, BLAKE3Pipeline::hash("omw-data-weights"), 0, "application/x-omw", "multi", "Open Multilingual WordNet", "utf-8"});
            cs.flush();
        }

        // 2. Parse WordNet
        std::vector<Synset> synsets;
        parse_wordnet_file(wordnet_dir + "/data.noun", 'n', synsets);
        parse_wordnet_file(wordnet_dir + "/data.verb", 'v', synsets);
        parse_wordnet_file(wordnet_dir + "/data.adj",  'a', synsets);
        parse_wordnet_file(wordnet_dir + "/data.adv",  'r', synsets);

        // 3. Build WordNet
        std::unordered_map<std::string, CachedComp> synset_comps;
        std::cout << "[INFO] Building WordNet substrate graph..." << std::endl;
        RecordBatch main_batch;
        
        for (const auto& syn : synsets) {
            std::string key = syn.offset + "-" + (syn.pos == 's' ? 'a' : syn.pos);
            CachedComp s_comp = get_or_create_comp(key, lookup, main_batch);
            if (s_comp.comp_id == BLAKE3Pipeline::Hash{}) continue;
            synset_comps[key] = s_comp;

            if (!syn.gloss.empty()) {
                CachedComp g_comp = get_or_create_comp(syn.gloss, lookup, main_batch);
                if (g_comp.comp_id != BLAKE3Pipeline::Hash{}) queue_relation(s_comp, g_comp, "has_gloss", wn_content_id, main_batch, 1800.0);
            }
            for (const auto& lemma : syn.lemmas) {
                CachedComp l_comp = get_or_create_comp(lemma, lookup, main_batch);
                if (l_comp.comp_id != BLAKE3Pipeline::Hash{}) queue_relation(l_comp, s_comp, "lemma_of", wn_content_id, main_batch, 1900.0);
            }
            if (main_batch.comp.size() > 5000) flush_batch(db, main_batch);
        }
        flush_batch(db, main_batch);

        std::cout << "[INFO] Linking semantic relations..." << std::endl;
        for (const auto& syn : synsets) {
            std::string key = syn.offset + "-" + (syn.pos == 's' ? 'a' : syn.pos);
            auto sit = synset_comps.find(key);
            if (sit == synset_comps.end()) continue;
            for (const auto& ptr : syn.pointers) {
                std::string tkey = ptr.target_offset + "-" + (ptr.target_pos == 's' ? 'a' : ptr.target_pos);
                auto tit = synset_comps.find(tkey);
                if (tit != synset_comps.end()) queue_relation(sit->second, tit->second, ptr.type, wn_content_id, main_batch, 1700.0);
            }
            if (main_batch.rel.size() > 5000) flush_batch(db, main_batch);
        }
        flush_batch(db, main_batch);

        // 4. Parallel OMW
        std::vector<std::string> omw_files;
        for (const auto& entry : std::filesystem::recursive_directory_iterator(omw_data_dir)) {
            if (entry.is_regular_file() && entry.path().extension() == ".tab" && 
                entry.path().filename().string().rfind("wn-data-", 0) == 0) {
                omw_files.push_back(entry.path().string());
            }
        }

        size_t num_threads = std::max(1u, std::thread::hardware_concurrency());
        std::cout << "[INFO] Parallelizing OMW across " << num_threads << " threads..." << std::endl;
        
        std::vector<std::vector<std::string>> chunks(num_threads);
        for (size_t i = 0; i < omw_files.size(); ++i) chunks[i % num_threads].push_back(omw_files[i]);

        std::vector<std::thread> workers;
        for (size_t i = 0; i < num_threads; ++i) {
            workers.emplace_back(omw_worker, std::ref(chunks[i]), std::ref(lookup), std::ref(synset_comps), omw_content_id);
        }
        for (auto& w : workers) w.join();

        std::cout << "[SUCCESS] Ingestion Complete." << std::endl;
        std::cout << "  Compositions: " << g_comp_count << std::endl;
        std::cout << "  Relations:    " << g_rel_count << std::endl;

    } catch (const std::exception& ex) {
        std::cerr << "[FATAL] " << ex.what() << std::endl;
        return 1;
    }
    return 0;
}
