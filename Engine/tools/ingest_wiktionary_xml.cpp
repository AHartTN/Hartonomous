// ingest_wiktionary_xml.cpp
// High-performance deep-dive streaming XML parser for Wiktionary
// Refactored to use centralized SubstrateService and AsyncFlusher.

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
#include <chrono>
#include <regex>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <queue>
#include <algorithm>
#include <cstring>
#include <omp.h>
#include <atomic>

namespace Hartonomous {

using namespace hartonomous::spatial;
using Clock = std::chrono::steady_clock;
using Service = SubstrateService;

// ─────────────────────────────────────────────
// Substrate Integration
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

void merge_comp(const Service::ComputedComp& cc, const std::string& text, SubstrateBatch& batch) {
    if (!cc.valid) return;
    if (!g_cache.exists_comp(cc.comp.id)) {
        if (!g_cache.exists_phys(cc.comp.physicality_id)) {
            batch.phys.push_back(cc.phys); g_cache.add_phys(cc.comp.physicality_id);
        }
        batch.comp.push_back(cc.comp); batch.seq.insert(batch.seq.end(), cc.seq.begin(), cc.seq.end());
        g_cache.add_comp(cc.comp.id); g_comp_count++;
    }
    g_cache.cache_comp(text, cc.cache_entry);
}

void merge_relation(const Service::ComputedRelation& cr, const BLAKE3Pipeline::Hash& content_id, SubstrateBatch& batch) {
    if (!cr.valid) return;
    if (!g_cache.exists_rel(cr.rel.id)) {
        if (!g_cache.exists_phys(cr.rel.physicality_id)) {
            batch.phys.push_back(cr.phys); g_cache.add_phys(cr.rel.physicality_id);
        }
        batch.rel.push_back(cr.rel); batch.rel_seq.insert(batch.rel_seq.end(), cr.seq.begin(), cr.seq.end());
        batch.rating.push_back(cr.rating); g_cache.add_rel(cr.rel.id); g_rel_count++;
    }
    EvidenceKey ev_key{content_id, cr.rel.id};
    if (g_evidence_cache.find(ev_key) == g_evidence_cache.end()) {
        batch.evidence.push_back(cr.evidence); g_evidence_cache.insert(ev_key);
    }
}

// ─────────────────────────────────────────────
// Wiktionary Advanced Parsing
// ─────────────────────────────────────────────

std::string clean_markup(const std::string& input) {
    static const std::regex r_temp_simple("\\{\\{[^|}]+\\|([^|}]*)(\\|[^}]*)?\\}\\}");
    std::string s = std::regex_replace(input, r_temp_simple, "$1");
    static const std::regex r_temp_cleanup("\\{\\{[^}]+\\}\\}");
    s = std::regex_replace(s, r_temp_cleanup, "");
    static const std::regex r_link("\\[\\[([^|\\]]+)(?:\\|[^|\\]]+)?\\]\\]");
    s = std::regex_replace(s, r_link, "$1");
    static const std::regex r_bold("'''"); s = std::regex_replace(s, r_bold, "");
    static const std::regex r_italics("''"); s = std::regex_replace(s, r_italics, "");
    static const std::regex r_amp("&amp;"); s = std::regex_replace(s, r_amp, "&");
    static const std::regex r_lt("&lt;"); s = std::regex_replace(s, r_lt, "<");
    static const std::regex r_gt("&gt;"); s = std::regex_replace(s, r_gt, ">");
    static const std::regex r_quot("&quot;"); s = std::regex_replace(s, r_quot, "\"");
    s.erase(0, s.find_first_not_of(" \t\r\n"));
    size_t last = s.find_last_not_of(" \t\r\n");
    if (last != std::string::npos) s.erase(last + 1); else s.clear();
    return s;
}

struct Page { std::string title; std::string text; };
struct ProcessedPage { std::string title_word; Service::ComputedComp title_comp; struct Rel { std::string target; Service::ComputedComp t_comp; double rating; }; std::vector<Rel> rels; };

ProcessedPage process_page_compute(const Page& page, AtomLookup& lookup) {
    ProcessedPage res; std::string word = page.title;
    if (word.find("Thesaurus:") == 0) word = word.substr(10);
    if (word.find("Category:") == 0) word = word.substr(9);
    res.title_word = word; res.title_comp = Service::compute_comp(word, lookup);
    if (!res.title_comp.valid) return res;
    std::istringstream iss(page.text); std::string line; bool in_eng = false;
    while (std::getline(iss, line)) {
        if (line.compare(0, 10, "==English==") == 0) in_eng = true;
        else if (line.compare(0, 2, "==") == 0 && line.compare(0, 3, "===") != 0) in_eng = false;
        bool is_cat = (line.find("[[Category:") != std::string::npos);
        if (!in_eng && !is_cat) continue;
        static const std::vector<std::pair<std::string, double>> rel_types = {{"synonyms", 1950.0}, {"antonyms", 1850.0}, {"hypernyms", 1900.0}, {"hyponyms", 1800.0}, {"meronyms", 1850.0}, {"holonyms", 1850.0}, {"coordinate terms", 1750.0}, {"derived terms", 1600.0}, {"related terms", 1550.0}};
        for (const auto& [rtype, rating] : rel_types) {
            if (line.find("{{" + rtype + "|") != std::string::npos) {
                std::regex r_ext("\\{\\{" + rtype + "\\|[^|]+\\|([^}]+)\\}\\}"); std::smatch m;
                if (std::regex_search(line, m, r_ext)) {
                    std::istringstream tiss(m[1].str()); std::string trg;
                    while (std::getline(tiss, trg, '|')) { if (trg.find('=') == std::string::npos) { std::string clean = clean_markup(trg); res.rels.push_back({clean, Service::compute_comp(clean, lookup), rating}); } }
                }
            }
        }
        if (line.find("{{ws|") != std::string::npos) {
            static const std::regex r_ws("\\{\\{ws\\|[^|]+\\|([^}|]+)");
            for (auto i = std::sregex_iterator(line.begin(), line.end(), r_ws); i != std::sregex_iterator(); ++i) { std::string clean = clean_markup((*i)[1].str()); res.rels.push_back({clean, Service::compute_comp(clean, lookup), 1850.0}); }
        }
        if (line.size() > 2 && line[0] == '#' && line[1] == ' ') { std::string def = clean_markup(line.substr(2)); if (!def.empty()) res.rels.push_back({def, Service::compute_comp(def, lookup), 1900.0}); }
        if (is_cat) { static const std::regex r_cat("\\[\\[Category:([^|\\]]+)"); std::smatch m; if (std::regex_search(line, m, r_cat)) { std::string clean = m[1].str(); res.rels.push_back({clean, Service::compute_comp(clean, lookup), 1200.0}); } }
    }
    return res;
}

} // namespace Hartonomous

int main(int argc, char** argv) {
    if (argc < 2) { std::cerr << "Usage: " << argv[0] << " <xml>" << std::endl; return 1; }
    using namespace Hartonomous;
    std::string xml_path = argv[1]; Timer total_timer;

    try {
        PostgresConnection db;
        db.execute("SET synchronous_commit = off");
        db.execute("SET work_mem = '512MB'");
        db.execute("SET maintenance_work_mem = '2GB'");

        AtomLookup lookup(db); lookup.preload_all();
        g_cache.pre_populate(db);

        BLAKE3Pipeline::Hash content_id = BLAKE3Pipeline::hash("source:wiktionary");
        { ContentStore cs(db, false, false); cs.store({content_id, BLAKE3Pipeline::hash("t:sys"), BLAKE3Pipeline::hash("u:cur"), 5, BLAKE3Pipeline::hash("wkt-w"), 0, "text/xml", "en", "Wiktionary", "utf-8"}); cs.flush(); }

        AsyncFlusher flusher;
        std::ifstream in(xml_path, std::ios::binary); if (!in) return 1;
        std::string line, cur_title, cur_text; int cur_ns = -1; bool in_text = false;
        std::vector<Page> chunk;
        size_t page_count = 0; static constexpr size_t CHUNK_SIZE = 10000;

        std::cout << "[Phase 1] Streaming Wiktionary (parallel)..." << std::endl;
        while (std::getline(in, line)) {
            if (line.find("<title>") != std::string::npos) {
                size_t s = line.find("<title>") + 7, e = line.find("</title>");
                cur_title = line.substr(s, e - s); cur_text.clear(); cur_ns = -1;
            } else if (line.find("<ns>") != std::string::npos) {
                size_t s = line.find("<ns>") + 4, e = line.find("</ns>");
                try { cur_ns = std::stoi(line.substr(s, e - s)); } catch (...) { cur_ns = -1; }
            } else if (line.find("<text") != std::string::npos) {
                if (cur_ns != 0 && cur_ns != 14 && cur_ns != 110) { in_text = false; continue; }
                in_text = true; size_t s = line.find('>') + 1; cur_text = line.substr(s);
                if (line.find("</text>") != std::string::npos) { in_text = false; cur_text.erase(cur_text.find("</text>")); chunk.push_back({cur_title, cur_text}); }
            } else if (in_text) {
                if (line.find("</text>") != std::string::npos) { in_text = false; cur_text += line.substr(0, line.find("</text>")); chunk.push_back({cur_title, cur_text}); }
                else { cur_text += line + "\n"; }
            }

            if (chunk.size() >= CHUNK_SIZE) {
                std::vector<ProcessedPage> results(chunk.size());
                #pragma omp parallel for schedule(dynamic, 16)
                for (size_t i = 0; i < chunk.size(); ++i) results[i] = process_page_compute(chunk[i], lookup);
                auto batch = std::make_unique<SubstrateBatch>();
                for (size_t i = 0; i < chunk.size(); ++i) {
                    const auto& pr = results[i]; merge_comp(pr.title_comp, pr.title_word, *batch);
                    auto sit = g_cache.get_comp(pr.title_word); if (!sit) continue;
                    for (const auto& rel : pr.rels) { merge_comp(rel.t_comp, rel.target, *batch); auto tit = g_cache.get_comp(rel.target); if (tit) merge_relation(Service::compute_relation(*sit, *tit, content_id, rel.rating), content_id, *batch); }
                }
                flusher.enqueue(std::move(batch));
                page_count += chunk.size();
                if (page_count % 50000 == 0) std::cout << "  Processed " << page_count << " pages..." << std::endl;
                chunk.clear();
            }
        }
        if (!chunk.empty()) {
            std::vector<ProcessedPage> results(chunk.size());
            #pragma omp parallel for schedule(dynamic, 16)
            for (size_t i = 0; i < chunk.size(); ++i) results[i] = process_page_compute(chunk[i], lookup);
            auto batch = std::make_unique<SubstrateBatch>();
            for (size_t i = 0; i < chunk.size(); ++i) {
                const auto& pr = results[i]; merge_comp(pr.title_comp, pr.title_word, *batch);
                auto sit = g_cache.get_comp(pr.title_word); if (!sit) continue;
                for (const auto& rel : pr.rels) { merge_comp(rel.t_comp, rel.target, *batch); auto tit = g_cache.get_comp(rel.target); if (tit) merge_relation(Service::compute_relation(*sit, *tit, content_id, rel.rating), content_id, *batch); }
            }
            flusher.enqueue(std::move(batch));
        }
        flusher.wait_all();
        std::cout << "[SUCCESS] Wiktionary complete in " << total_timer.elapsed_sec() << "s" << std::endl;
    } catch (const std::exception& ex) { std::cerr << "[FATAL] " << ex.what() << std::endl; return 1; }
    return 0;
}