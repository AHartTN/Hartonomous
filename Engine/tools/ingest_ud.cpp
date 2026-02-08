// ingest_ud.cpp
// Bulk ingestion for Universal Dependencies (CoNLL-U)
// Architecture: Word-level compositions with dependency AND adjacency relations.
//   - Each lemma becomes a word-level composition
//   - Dependency relations capture syntactic structure (head→dependent, ELO 1800)
//   - Adjacency relations capture word order (consecutive tokens, ELO 1500)

#include <database/postgres_connection.hpp>
#include <storage/atom_lookup.hpp>
#include <storage/content_store.hpp>
#include <ingestion/substrate_service.hpp>
#include <ingestion/substrate_cache.hpp>
#include <ingestion/async_flusher.hpp>
#include <utils/time.hpp>
#include <utils/unicode.hpp>

#include <iostream>
#include <iomanip>
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

// ─────────────────────────────────────────────
// Global Caches
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
// Merge Helper
// ─────────────────────────────────────────────

void merge_comp(const Service::ComputedComp& cc, SubstrateBatch& batch) {
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
        g_cache.add_rel(cr.rel.id);
        g_rel_count++;
    }
    // Always push rating — accumulates observations for repeated word pairs
    batch.rating.push_back(cr.rating);
    EvidenceKey ev_key{content_id, cr.rel.id};
    if (g_evidence_cache.find(ev_key) == g_evidence_cache.end()) {
        batch.evidence.push_back(cr.evidence);
        g_evidence_cache.insert(ev_key);
    }
}

// ─────────────────────────────────────────────
// Parser
// ─────────────────────────────────────────────

struct Token { uint32_t id; std::string lemma; uint32_t head; };

void parse_conllu(const std::string& path, std::vector<std::vector<Token>>& sentences) {
    std::ifstream in(path); if (!in) return;
    std::string line; std::vector<Token> current;
    while (std::getline(in, line)) {
        if (line.empty()) { if (!current.empty()) { sentences.push_back(std::move(current)); current.clear(); } continue; }
        if (line[0] == '#') continue;
        std::istringstream iss(line); std::string id_s, form, lemma, upos, xpos, feats, head_s, deprel, deps, misc;
        if (!(iss >> id_s >> form >> lemma >> upos >> xpos >> feats >> head_s >> deprel >> deps >> misc)) continue;
        if (id_s.find('.') != std::string::npos || id_s.find('-') != std::string::npos) continue;
        try { uint32_t id = std::stoul(id_s), head = (head_s == "0") ? 0 : std::stoul(head_s); current.push_back({id, lemma, head}); } catch (...) { continue; }
    }
    if (!current.empty()) sentences.push_back(std::move(current));
}

} // namespace Hartonomous

int main(int argc, char** argv) {
    if (argc < 2) { std::cerr << "Usage: " << argv[0] << " <ud_dir>" << std::endl; return 1; }
    using namespace Hartonomous;
    std::string ud_dir = argv[1];
    Timer total_timer;

    try {
        PostgresConnection db;
        db.execute("SET synchronous_commit = off");
        db.execute("SET work_mem = '512MB'");
        db.execute("SET maintenance_work_mem = '2GB'");

        AtomLookup lookup(db); lookup.preload_all();
        g_cache.pre_populate(db);

        BLAKE3Pipeline::Hash ud_content_id = BLAKE3Pipeline::hash("source:universal-dependencies");
        { ContentStore cs(db, false, false); cs.store({ud_content_id, BLAKE3Pipeline::hash("t:sys"), BLAKE3Pipeline::hash("u:cur"), 4, BLAKE3Pipeline::hash("ud-w"), 0, "text/x-conllu", "multi", "UD", "utf-8"}); cs.flush(); }

        AsyncFlusher flusher;
        std::vector<std::string> files;
        for (const auto& entry : std::filesystem::recursive_directory_iterator(ud_dir))
            if (entry.is_regular_file() && entry.path().extension() == ".conllu") files.push_back(entry.path().string());

        std::cout << "[Phase 1] Processing " << files.size() << " CoNLL-U files (parallel)..." << std::endl;
        size_t processed = 0; static constexpr size_t CHUNK_SIZE = 100;

        for (size_t f_start = 0; f_start < files.size(); f_start += CHUNK_SIZE) {
            size_t f_end = std::min(f_start + CHUNK_SIZE, files.size());
            struct FileResult { std::vector<std::vector<Token>> sents; std::vector<std::vector<Service::ComputedComp>> c_comps; };
            std::vector<FileResult> results(f_end - f_start);

            #pragma omp parallel for schedule(dynamic)
            for (size_t i = f_start; i < f_end; ++i) {
                parse_conllu(files[i], results[i - f_start].sents);
                for (const auto& sent : results[i - f_start].sents) {
                    std::vector<Service::ComputedComp> sc;
                    for (const auto& tok : sent)
                        sc.push_back(Service::compute_comp(tok.lemma, lookup));
                    results[i - f_start].c_comps.push_back(std::move(sc));
                }
            }

            auto batch = std::make_unique<SubstrateBatch>();
            for (size_t i = 0; i < results.size(); ++i) {
                for (size_t si = 0; si < results[i].sents.size(); ++si) {
                    const auto& sent = results[i].sents[si];
                    const auto& c_comps = results[i].c_comps[si];

                    // Merge word compositions + build token map for dependency relations
                    std::unordered_map<uint32_t, Service::CachedComp> token_comps;
                    for (size_t ti = 0; ti < sent.size(); ++ti) {
                        merge_comp(c_comps[ti], *batch);
                        if (c_comps[ti].valid)
                            token_comps[sent[ti].id] = c_comps[ti].cache_entry;
                    }

                    // Dependency relations (syntactic structure, ELO 1800)
                    for (const auto& tok : sent) {
                        if (tok.head != 0) {
                            auto head_it = token_comps.find(tok.head), dep_it = token_comps.find(tok.id);
                            if (head_it != token_comps.end() && dep_it != token_comps.end())
                                merge_relation(Service::compute_relation(head_it->second, dep_it->second, ud_content_id, 1800.0), ud_content_id, *batch);
                        }
                    }

                    // Adjacency relations (word order, ELO 1500)
                    for (size_t ti = 0; ti + 1 < c_comps.size(); ++ti) {
                        if (c_comps[ti].valid && c_comps[ti + 1].valid &&
                            c_comps[ti].comp.id != c_comps[ti + 1].comp.id) {
                            merge_relation(Service::compute_relation(
                                c_comps[ti].cache_entry, c_comps[ti + 1].cache_entry,
                                ud_content_id, 1500.0), ud_content_id, *batch);
                        }
                    }
                }
            }
            flusher.enqueue(std::move(batch));
            processed += (f_end - f_start);
            if (processed % 500 == 0)
                std::cout << "  Processed " << processed << " files (" << g_comp_count << " comps, " << g_rel_count << " rels)" << std::endl;
        }
        flusher.wait_all();
        std::cout << "[SUCCESS] UD complete in " << total_timer.elapsed_sec() << "s" << std::endl;
        std::cout << "  Total compositions: " << g_comp_count << " | Total relations: " << g_rel_count << std::endl;
    } catch (const std::exception& ex) { std::cerr << "[FATAL] " << ex.what() << std::endl; return 1; }
    return 0;
}
