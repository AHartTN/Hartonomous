// ingest_tatoeba.cpp
// High-performance bulk ingestion for Tatoeba translation sentences
// Architecture: Word-level decomposition with adjacency relations.
//   - Each sentence decomposed into word-level compositions
//   - Adjacency relations capture word order (grammar patterns)
//   - Translation links create cross-lingual word co-occurrence relations
//   - CJK characters tokenized individually (they ARE semantic units)

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
#include <string>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <filesystem>
#include <algorithm>
#include <cstdlib>
#include <cstring>
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

// Per-sentence: store the word compositions for translation link processing
struct SentenceWords {
    std::vector<Service::CachedComp> words;
};
std::unordered_map<uint32_t, SentenceWords> g_id_to_words;

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

} // namespace Hartonomous

int main(int argc, char** argv) {
    if (argc < 3) { std::cerr << "Usage: " << argv[0] << " <sentences.csv> <links.csv>" << std::endl; return 1; }
    using namespace Hartonomous;
    std::string sentences_file = argv[1], links_file = argv[2];
    Timer total_timer;

    try {
        PostgresConnection db;
        db.execute("SET synchronous_commit = off");
        db.execute("SET work_mem = '512MB'");
        db.execute("SET maintenance_work_mem = '2GB'");

        AtomLookup lookup(db);
        std::cout << "[Phase 0] Preloading atoms..." << std::flush;
        Timer t0; lookup.preload_all();
        std::cout << " (" << t0.elapsed_ms() << "ms)" << std::endl;

        g_cache.pre_populate(db);

        BLAKE3Pipeline::Hash tatoeba_content_id = BLAKE3Pipeline::hash("source:tatoeba");
        {
            ContentStore cs(db, false, false);
            cs.store({tatoeba_content_id, BLAKE3Pipeline::hash("t:sys"), BLAKE3Pipeline::hash("u:cur"), 3, BLAKE3Pipeline::hash("tatoeba-w"), 0, "text/tsv", "multi", "Tatoeba", "utf-8"});
            cs.flush();
        }

        AsyncFlusher flusher;

        // Phase 1: Decompose sentences into word-level compositions + adjacency relations
        std::cout << "[Phase 1] Decomposing Tatoeba sentences (word-level, parallel)..." << std::endl;
        std::ifstream sin(sentences_file); std::string line;
        static constexpr size_t CHUNK_SIZE = 200000;
        g_id_to_words.reserve(14000000);

        struct SentenceEntry { uint32_t sid; std::string text; };
        std::vector<SentenceEntry> chunk;
        chunk.reserve(CHUNK_SIZE);
        size_t total_sentences = 0;

        auto process_chunk = [&]() {
            // Parallel: decompose each sentence into words
            std::vector<Service::SentenceDecomposition> decomps(chunk.size());
            #pragma omp parallel for schedule(dynamic, 64)
            for (size_t i = 0; i < chunk.size(); ++i)
                decomps[i] = Service::decompose_sentence(chunk[i].text, lookup);

            // Serial: merge word compositions + adjacency relations
            auto batch = std::make_unique<SubstrateBatch>();
            for (size_t i = 0; i < chunk.size(); ++i) {
                auto& d = decomps[i];

                // Store word CachedComps for Phase 2 translation links
                SentenceWords sw;
                for (const auto& wc : d.word_comps) {
                    merge_comp(wc, *batch);
                    if (wc.valid) sw.words.push_back(wc.cache_entry);
                }
                if (!sw.words.empty()) g_id_to_words[chunk[i].sid] = std::move(sw);

                // Adjacency relations (word order patterns, ELO 1500)
                for (const auto& [ai, bi] : d.adjacency) {
                    merge_relation(Service::compute_relation(
                        d.word_comps[ai].cache_entry,
                        d.word_comps[bi].cache_entry,
                        tatoeba_content_id, 1500.0), tatoeba_content_id, *batch);
                }
            }
            flusher.enqueue(std::move(batch));
            total_sentences += chunk.size();
            if (total_sentences % 500000 == 0)
                std::cout << "  Processed " << total_sentences << " sentences (" << g_comp_count << " comps, " << g_rel_count << " rels)" << std::endl;
            chunk.clear();
        };

        while (std::getline(sin, line)) {
            if (line.empty()) continue;
            const char* p = line.c_str();
            char* end;
            uint32_t sid = static_cast<uint32_t>(std::strtoul(p, &end, 10));
            if (*end != '\t') continue;
            const char* t2 = std::strchr(end + 1, '\t');
            if (!t2) continue;
            chunk.push_back({sid, std::string(t2 + 1)});

            if (chunk.size() >= CHUNK_SIZE) process_chunk();
        }
        if (!chunk.empty()) process_chunk();
        flusher.wait_all();
        std::cout << "  Phase 1 complete: " << total_sentences << " sentences → "
                  << g_comp_count << " compositions, " << g_rel_count << " relations" << std::endl;

        // Phase 2: Translation links → cross-lingual word relations
        // For each translation pair, create relations between overlapping word compositions.
        // Uses "representative words" approach: relate first content word of each sentence.
        std::cout << "[Phase 2] Processing Tatoeba translation links..." << std::endl;
        std::ifstream lin(links_file);
        std::vector<std::pair<uint32_t, uint32_t>> link_chunk;
        link_chunk.reserve(CHUNK_SIZE);
        size_t total_links = 0, valid_links = 0;

        auto process_links = [&]() {
            // Parallel: compute relations between representative words of each pair
            struct LinkResult { Service::ComputedRelation rel; bool valid = false; };
            std::vector<LinkResult> results(link_chunk.size());

            #pragma omp parallel for schedule(dynamic, 256)
            for (size_t i = 0; i < link_chunk.size(); ++i) {
                auto it1 = g_id_to_words.find(link_chunk[i].first);
                auto it2 = g_id_to_words.find(link_chunk[i].second);
                if (it1 == g_id_to_words.end() || it2 == g_id_to_words.end()) continue;
                if (it1->second.words.empty() || it2->second.words.empty()) continue;

                // Create cross-lingual relations between each word pair up to a budget
                // This captures the translation signal at word level
                const auto& w1 = it1->second.words;
                const auto& w2 = it2->second.words;
                size_t budget = std::min(size_t(4), std::min(w1.size(), w2.size()));
                // Just mark the first pair for the parallel result; rest done in serial
                results[i].rel = Service::compute_relation(w1[0], w2[0], tatoeba_content_id, 1400.0);
                results[i].valid = true;
            }

            auto batch = std::make_unique<SubstrateBatch>();
            for (size_t i = 0; i < link_chunk.size(); ++i) {
                if (!results[i].valid) continue;
                merge_relation(results[i].rel, tatoeba_content_id, *batch);

                // Additional cross-lingual word pairs (serial, small budget)
                auto it1 = g_id_to_words.find(link_chunk[i].first);
                auto it2 = g_id_to_words.find(link_chunk[i].second);
                if (it1 == g_id_to_words.end() || it2 == g_id_to_words.end()) continue;
                const auto& w1 = it1->second.words;
                const auto& w2 = it2->second.words;
                size_t budget = std::min(size_t(4), std::min(w1.size(), w2.size()));
                for (size_t j = 1; j < budget; ++j) {
                    merge_relation(Service::compute_relation(w1[j], w2[j], tatoeba_content_id, 1300.0),
                                   tatoeba_content_id, *batch);
                }
                valid_links++;
            }
            flusher.enqueue(std::move(batch));
            total_links += link_chunk.size();
            if (total_links % 2000000 == 0)
                std::cout << "  Processed " << total_links << " links (" << valid_links << " valid)" << std::endl;
            link_chunk.clear();
        };

        while (std::getline(lin, line)) {
            if (line.empty()) continue;
            const char* p = line.c_str();
            char* end;
            uint32_t id1 = static_cast<uint32_t>(std::strtoul(p, &end, 10));
            if (*end != '\t') continue;
            uint32_t id2 = static_cast<uint32_t>(std::strtoul(end + 1, &end, 10));
            link_chunk.emplace_back(id1, id2);

            if (link_chunk.size() >= CHUNK_SIZE) process_links();
        }
        if (!link_chunk.empty()) process_links();
        flusher.wait_all();
        std::cout << "  Phase 2 complete: " << valid_links << " valid translation links → " << g_rel_count << " total relations" << std::endl;

        std::cout << "[SUCCESS] Tatoeba complete in " << total_timer.elapsed_sec() << "s" << std::endl;
        std::cout << "  Total compositions: " << g_comp_count << " | Total relations: " << g_rel_count << std::endl;

    } catch (const std::exception& ex) { std::cerr << "[FATAL] " << ex.what() << std::endl; return 1; }
    return 0;
}
