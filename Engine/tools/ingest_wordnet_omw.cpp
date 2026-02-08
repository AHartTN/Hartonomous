// ingest_wordnet_omw.cpp
// High-performance bulk ingestion for Princeton WordNet 3.0 + OMW-data
// Centralized Architecture: SubstrateCache + SubstrateService + AsyncFlusher

#include <database/postgres_connection.hpp>
#include <storage/atom_lookup.hpp>
#include <storage/content_store.hpp>
#include <ingestion/substrate_service.hpp>
#include <ingestion/substrate_cache.hpp>
#include <ingestion/async_flusher.hpp>
#include <utils/time.hpp>
#include <utils/unicode.hpp>

#include <iostream>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <filesystem>
#include <algorithm>
#include <omp.h>
#include <atomic>

namespace Hartonomous {

using Service = SubstrateService;

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

// ─────────────────────────────────────────────
// State
// ─────────────────────────────────────────────

SubstrateCache g_cache;

struct EvidenceKey {
    BLAKE3Pipeline::Hash content_id;
    BLAKE3Pipeline::Hash rel_id;
    bool operator==(const EvidenceKey& o) const { return content_id == o.content_id && rel_id == o.rel_id; }
};

struct EvidenceKeyHasher {
    size_t operator()(const EvidenceKey& k) const {
        size_t h = HashHasher{}(k.content_id);
        return h ^ (HashHasher{}(k.rel_id) + 0x9e3779b9 + (h << 6) + (h >> 2));
    }
};

std::unordered_set<EvidenceKey, EvidenceKeyHasher> g_evidence_cache;
std::atomic<size_t> g_comp_count{0};
std::atomic<size_t> g_rel_count{0};

// ─────────────────────────────────────────────
// Merge Logic
// ─────────────────────────────────────────────

void merge_comp(const Service::ComputedComp& cc, const std::string& text, SubstrateBatch& batch) {
    if (!cc.valid) return;
    if (!g_cache.exists_comp(cc.comp.id)) {
        if (!g_cache.exists_phys(cc.comp.physicality_id)) {
            batch.phys.push_back(cc.phys);
            g_cache.add_phys(cc.comp.physicality_id);
        }
        batch.comp.push_back(cc.comp);
        batch.seq.insert(batch.seq.end(), cc.seq.begin(), cc.seq.end());
        g_cache.add_comp(cc.comp.id);
        g_comp_count++;
    }
    g_cache.cache_comp(text, cc.cache_entry);
}

void merge_relation(const Service::ComputedRelation& cr, const BLAKE3Pipeline::Hash& content_id, SubstrateBatch& batch) {
    if (!cr.valid) return;
    if (!g_cache.exists_rel(cr.rel.id)) {
        if (!g_cache.exists_phys(cr.rel.physicality_id)) {
            batch.phys.push_back(cr.phys);
            g_cache.add_phys(cr.rel.physicality_id);
        }
        batch.rel.push_back(cr.rel);
        batch.rel_seq.insert(batch.rel_seq.end(), cr.seq.begin(), cr.seq.end());
        batch.rating.push_back(cr.rating);
        g_cache.add_rel(cr.rel.id);
        g_rel_count++;
    }

    EvidenceKey ev_key{content_id, cr.rel.id};
    if (g_evidence_cache.find(ev_key) == g_evidence_cache.end()) {
        batch.evidence.push_back(cr.evidence);
        g_evidence_cache.insert(ev_key);
    }
}

// ─────────────────────────────────────────────
// Parser
// ─────────────────────────────────────────────

void parse_wordnet_file(const std::string& file, char expected_pos, std::vector<Synset>& synsets) {
    std::ifstream in(file); if (!in) return;
    std::string line;
    while (std::getline(in, line)) {
        if (line.empty() || line[0] == ' ' || line[0] == '#') continue;
        size_t pipe_pos = line.find('|');
        std::string synset_part = (pipe_pos == std::string::npos) ? line : line.substr(0, pipe_pos);
        std::string gloss = (pipe_pos == std::string::npos) ? "" : line.substr(pipe_pos + 1);
        std::istringstream iss(synset_part);
        std::string offset, lex_filenum, word_count_hex; char pos;
        if (!(iss >> offset >> lex_filenum >> pos >> word_count_hex)) continue;
        if (pos != expected_pos && !(pos == 's' && expected_pos == 'a')) continue;
        int word_count = std::stoi(word_count_hex, nullptr, 16);
        std::vector<std::string> lemmas; lemmas.reserve(word_count);
        for (int i = 0; i < word_count; ++i) { std::string lemma, lex_id_hex; iss >> lemma >> lex_id_hex; lemmas.push_back(std::move(lemma)); }
        int pointer_count; iss >> pointer_count;
        std::vector<Synset::Pointer> pointers; pointers.reserve(pointer_count);
        for (int i = 0; i < pointer_count; ++i) { std::string sym, target_offset, src_trg; char target_pos; iss >> sym >> target_offset >> target_pos >> src_trg; pointers.push_back({sym, target_offset, target_pos}); }
        if (!gloss.empty()) {
            size_t s = gloss.find_first_not_of(" \t\r\n"), e = gloss.find_last_not_of(" \t\r\n");
            if (s != std::string::npos) gloss = gloss.substr(s, e - s + 1); else gloss.clear();
        }
        synsets.push_back({offset, pos, lemmas, gloss, pointers});
    }
}

} // namespace Hartonomous

int main(int argc, char** argv) {
    if (argc < 3) { std::cerr << "Usage: " << argv[0] << " <wn_dir> <omw_dir>" << std::endl; return 1; }
    using namespace Hartonomous;
    std::string wordnet_dir = argv[1], omw_data_dir = argv[2];
    Timer total_timer;

    try {
        PostgresConnection db;
        db.execute("SET synchronous_commit = off");
        db.execute("SET work_mem = '512MB'");
        db.execute("SET maintenance_work_mem = '2GB'");

        AtomLookup lookup(db); lookup.preload_all();
        g_cache.pre_populate(db);

        BLAKE3Pipeline::Hash wn_content_id = BLAKE3Pipeline::hash("source:wordnet-3.0");
        BLAKE3Pipeline::Hash omw_content_id = BLAKE3Pipeline::hash("source:omw-data");
        {
            ContentStore cs(db, false, false);
            cs.store({wn_content_id, BLAKE3Pipeline::hash("t:sys"), BLAKE3Pipeline::hash("u:cur"), 2, BLAKE3Pipeline::hash("wn-w"), 0, "app/x-wn", "eng", "PWN 3.0", "ascii"});
            cs.store({omw_content_id, BLAKE3Pipeline::hash("t:sys"), BLAKE3Pipeline::hash("u:cur"), 2, BLAKE3Pipeline::hash("omw-w"), 0, "app/x-omw", "multi", "OMW", "utf-8"});
            cs.flush();
        }

        AsyncFlusher flusher;

        std::cout << "[Phase 1] Parsing WordNet..." << std::flush;
        Timer t1; std::vector<Synset> synsets;
        parse_wordnet_file(wordnet_dir + "/data.noun", 'n', synsets);
        parse_wordnet_file(wordnet_dir + "/data.verb", 'v', synsets);
        parse_wordnet_file(wordnet_dir + "/data.adj",  'a', synsets);
        parse_wordnet_file(wordnet_dir + "/data.adv",  'r', synsets);
        std::cout << " " << synsets.size() << " synsets (" << t1.elapsed_ms() << "ms)" << std::endl;

        std::cout << "[Phase 2] Building WordNet (parallel compute)..." << std::endl;
        static constexpr size_t CHUNK_SIZE = 25000;
        for (size_t chunk_start = 0; chunk_start < synsets.size(); chunk_start += CHUNK_SIZE) {
            size_t chunk_end = std::min(chunk_start + CHUNK_SIZE, synsets.size());
            struct SynResult { Service::ComputedComp sc; Service::ComputedComp gc; std::vector<Service::ComputedComp> lcs; };
            std::vector<SynResult> chunk_results(chunk_end - chunk_start);

            #pragma omp parallel for schedule(dynamic, 64)
            for (size_t i = chunk_start; i < chunk_end; ++i) {
                const auto& syn = synsets[i];
                std::string key = syn.offset + "-" + static_cast<char>(syn.pos == 's' ? 'a' : syn.pos);
                chunk_results[i - chunk_start].sc = Service::compute_comp(key, lookup);
                if (!syn.gloss.empty()) chunk_results[i - chunk_start].gc = Service::compute_comp(syn.gloss, lookup);
                for (const auto& lemma : syn.lemmas) chunk_results[i - chunk_start].lcs.push_back(Service::compute_comp(lemma, lookup));
            }

            auto batch = std::make_unique<SubstrateBatch>();
            for (size_t i = chunk_start; i < chunk_end; ++i) {
                const auto& syn = synsets[i]; size_t ci = i - chunk_start;
                std::string key = syn.offset + "-" + static_cast<char>(syn.pos == 's' ? 'a' : syn.pos);
                merge_comp(chunk_results[ci].sc, key, *batch);
                auto sit = g_cache.get_comp(key); if (!sit) continue;
                if (!syn.gloss.empty()) {
                    merge_comp(chunk_results[ci].gc, syn.gloss, *batch);
                    auto git = g_cache.get_comp(syn.gloss);
                    if (git) merge_relation(Service::compute_relation(*sit, *git, wn_content_id, 1800.0), wn_content_id, *batch);
                }
                for (size_t li = 0; li < syn.lemmas.size(); ++li) {
                    merge_comp(chunk_results[ci].lcs[li], syn.lemmas[li], *batch);
                    auto lit = g_cache.get_comp(syn.lemmas[li]);
                    if (lit) merge_relation(Service::compute_relation(*lit, *sit, wn_content_id, 1900.0), wn_content_id, *batch);
                }
            }
            flusher.enqueue(std::move(batch));
            std::cout << "  [Phase 2] Processed " << chunk_end << "/" << synsets.size() << std::endl;
        }
        flusher.wait_all();

        std::cout << "[Phase 3] Linking relations..." << std::endl;
        for (size_t chunk_start = 0; chunk_start < synsets.size(); chunk_start += CHUNK_SIZE) {
            size_t chunk_end = std::min(chunk_start + CHUNK_SIZE, synsets.size());
            auto batch = std::make_unique<SubstrateBatch>();
            for (size_t i = chunk_start; i < chunk_end; ++i) {
                const auto& syn = synsets[i];
                std::string key = syn.offset + "-" + static_cast<char>(syn.pos == 's' ? 'a' : syn.pos);
                auto sit = g_cache.get_comp(key); if (!sit) continue;
                for (const auto& ptr : syn.pointers) {
                    std::string tkey = ptr.target_offset + "-" + static_cast<char>(ptr.target_pos == 's' ? 'a' : ptr.target_pos);
                    auto tit = g_cache.get_comp(tkey);
                    if (tit) merge_relation(Service::compute_relation(*sit, *tit, wn_content_id, 1700.0), wn_content_id, *batch);
                }
            }
            flusher.enqueue(std::move(batch));
        }
        flusher.wait_all();

        std::cout << "[Phase 4] Parsing OMW..." << std::flush;
        std::vector<std::string> omw_files;
        for (const auto& entry : std::filesystem::recursive_directory_iterator(omw_data_dir))
            if (entry.is_regular_file() && entry.path().extension() == ".tab") omw_files.push_back(entry.path().string());
        std::vector<std::vector<OMWEntry>> thread_omw(omp_get_max_threads());
        #pragma omp parallel for schedule(dynamic)
        for (size_t i = 0; i < omw_files.size(); ++i) {
            std::ifstream in(omw_files[i]); std::string line;
            while (std::getline(in, line)) {
                if (line.empty() || line[0] == '#') continue;
                size_t t1 = line.find('\t'), t2 = line.find('\t', t1+1);
                if (t2 == std::string::npos) continue;
                std::string sid = line.substr(0, t1), lemma = line.substr(t2+1);
                size_t e = lemma.find_last_not_of("\t\r\n "); if (e != std::string::npos) lemma.resize(e+1);
                size_t last = sid.find_last_of('-'); if (last != std::string::npos && last > 8) {
                    size_t prev = sid.find_last_of('-', last-1); if (prev != std::string::npos) sid = sid.substr(prev+1);
                }
                thread_omw[omp_get_thread_num()].push_back({sid, lemma});
            }
        }
        std::vector<OMWEntry> omw_entries;
        for (auto& v : thread_omw) omw_entries.insert(omw_entries.end(), std::make_move_iterator(v.begin()), std::make_move_iterator(v.end()));
        std::sort(omw_entries.begin(), omw_entries.end(), [](const OMWEntry& a, const OMWEntry& b) { return a.synset_id == b.synset_id ? a.lemma < b.lemma : a.synset_id < b.synset_id; });
        omw_entries.erase(std::unique(omw_entries.begin(), omw_entries.end(), [](const OMWEntry& a, const OMWEntry& b) { return a.synset_id == b.synset_id && a.lemma == b.lemma; }), omw_entries.end());
        std::cout << " " << omw_entries.size() << " entries." << std::endl;

        std::cout << "[Phase 5] Ingesting OMW (parallel compute)..." << std::endl;
        for (size_t chunk_start = 0; chunk_start < omw_entries.size(); chunk_start += CHUNK_SIZE) {
            size_t chunk_end = std::min(chunk_start + CHUNK_SIZE, omw_entries.size());
            std::vector<Service::ComputedComp> l_comps(chunk_end - chunk_start);
            #pragma omp parallel for schedule(dynamic, 64)
            for (size_t i = chunk_start; i < chunk_end; ++i) l_comps[i - chunk_start] = Service::compute_comp(omw_entries[i].lemma, lookup);
            auto batch = std::make_unique<SubstrateBatch>();
            for (size_t i = chunk_start; i < chunk_end; ++i) {
                const auto& entry = omw_entries[i]; auto sit = g_cache.get_comp(entry.synset_id);
                if (!sit) continue;
                merge_comp(l_comps[i - chunk_start], entry.lemma, *batch);
                auto lit = g_cache.get_comp(entry.lemma);
                if (lit) merge_relation(Service::compute_relation(*lit, *sit, omw_content_id, 1600.0), omw_content_id, *batch);
            }
            flusher.enqueue(std::move(batch));
        }
        flusher.wait_all();

        std::cout << "\n[SUCCESS] WordNet/OMW complete in " << total_timer.elapsed_sec() << "s" << std::endl;

    } catch (const std::exception& ex) { std::cerr << "[FATAL] " << ex.what() << std::endl; return 1; }
    return 0;
}
