#include <unicode/ingestor/ucd_processor.hpp>
#include <unicode/ingestor/node_generator.hpp>
#include <unicode/ingestor/semantic_sequencer.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <database/bulk_copy.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <iostream>
#include <algorithm>
#include <iomanip>
#include <thread>
#include <future>
#include <bitset>
#include <cstring>
#include <mutex>
#include <chrono>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

namespace Hartonomous::unicode {

UCDProcessor::UCDProcessor(const std::string& data_dir, PostgresConnection& db)
    : parser_(data_dir), db_(db) {}

static const char* HEX_TABLE = "0123456789abcdef";

static void format_uuid_to_string(const uint8_t* hash, std::string& out) {
    out.clear();
    out.reserve(36); 
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) out.push_back('-');
        out.push_back(HEX_TABLE[(hash[i] >> 4) & 0xF]);
        out.push_back(HEX_TABLE[hash[i] & 0xF]);
    }
}

static void geom_to_hex_fast(const Eigen::Vector4d& pt, std::string& out) {
    static constexpr uint8_t EWKB_HEADER[5] = { 0x01, 0x01, 0x00, 0x00, 0xC0 };
    out.clear();
    out.reserve(74);
    for (int i = 0; i < 5; ++i) {
        out.push_back(HEX_TABLE[(EWKB_HEADER[i] >> 4) & 0xF]);
        out.push_back(HEX_TABLE[EWKB_HEADER[i] & 0xF]);
    }
    const double coords[4] = {pt[0], pt[1], pt[2], pt[3]};
    for (int c = 0; c < 4; ++c) {
        const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&coords[c]);
        for (int i = 0; i < 8; ++i) {
            out.push_back(HEX_TABLE[(bytes[i] >> 4) & 0xF]);
            out.push_back(HEX_TABLE[bytes[i] & 0xF]);
        }
    }
}

struct ComputedRow {
    std::string phys_uuid;
    std::string hilbert;
    std::string centroid_hex;
    std::string atom_uuid;
    std::string codepoint_str;
};

void UCDProcessor::load_from_database() {
    std::cout << "Loading Unicode Gene Pool from database (ucd schema)..." << std::endl;
    auto& codepoints = parser_.get_codepoints_mutable();
    codepoints.clear();

    db_.query("SELECT codepoint, name, general_category, block, age, script, properties FROM ucd.code_points", [&](const std::vector<std::string>& row) {
        uint32_t cp = std::stoul(row[0]);
        CodepointMetadata meta;
        meta.codepoint = cp;
        meta.name = row[1];
        meta.general_category = row[2];
        meta.block = row[3];
        meta.age = row[4];
        meta.script = row[5];

        json props = json::parse(row[6]);
        auto get_str = [&](const char* key) { return props.contains(key) ? props[key].get<std::string>() : ""; };
        auto get_bool = [&](const char* key) { return props.contains(key) && props[key].get<std::string>() == "Y"; };

        meta.name1 = get_str("na1");
        std::string ccc = get_str("ccc");
        if (!ccc.empty()) meta.combining_class = static_cast<uint8_t>(std::stoul(ccc));

        meta.decomposition_type = get_str("dt");
        meta.decomposition_mapping = get_str("dm");
        meta.uppercase_mapping = get_str("uc");
        meta.lowercase_mapping = get_str("lc");
        meta.titlecase_mapping = get_str("tc");
        meta.simple_uppercase = get_str("suc");
        meta.simple_lowercase = get_str("slc");
        meta.simple_titlecase = get_str("stc");
        meta.simple_case_folding = get_str("scf");
        meta.case_folding = get_str("cf");
        meta.numeric_type = get_str("nt");
        meta.numeric_value = get_str("nv");
        meta.bidi_class = get_str("bc");
        meta.bidi_paired_bracket_type = get_str("bpt");
        meta.bidi_paired_bracket = get_str("bpb");
        meta.bidi_mirroring_glyph = get_str("bmg");
        meta.bidi_mirrored = get_bool("Bidi_M");
        meta.bidi_control = get_bool("Bidi_C");
        meta.joining_type = get_str("jt");
        meta.joining_group = get_str("jg");
        meta.join_control = get_bool("Join_C");
        meta.east_asian_width = get_str("ea");
        meta.line_break = get_str("lb");
        meta.word_break = get_str("WB");
        meta.sentence_break = get_str("SB");
        meta.grapheme_cluster_break = get_str("GCB");
        meta.indic_syllabic_category = get_str("InSC");
        meta.indic_positional_category = get_str("InPC");
        meta.vertical_orientation = get_str("vo");
        meta.hangul_syllable_type = get_str("hst");
        meta.jamo_short_name = get_str("JSN");

        meta.is_alphabetic = get_bool("Alpha");
        meta.is_uppercase = get_bool("Upper");
        meta.is_lowercase = get_bool("Lower");
        meta.is_cased = get_bool("Cased");
        meta.is_math = get_bool("Math");
        meta.is_hex_digit = get_bool("Hex");
        meta.is_ascii_hex_digit = get_bool("AHex");
        meta.is_ideographic = get_bool("Ideo");
        meta.is_unified_ideograph = get_bool("UIdeo");
        meta.is_radical = get_bool("Radical");
        meta.is_dash = get_bool("Dash");
        meta.is_whitespace = get_bool("WSpace");
        meta.is_quotation_mark = get_bool("QMark");
        meta.is_terminal_punctuation = get_bool("Term");
        meta.is_sentence_terminal = get_bool("STerm");
        meta.is_diacritic = get_bool("Dia");
        meta.is_extender = get_bool("Ext");
        meta.is_soft_dotted = get_bool("SD");
        meta.is_deprecated = get_bool("Dep");
        meta.is_default_ignorable = get_bool("DI");
        meta.is_variation_selector = get_bool("VS");
        meta.is_noncharacter = get_bool("NChar");
        meta.is_pattern_whitespace = get_bool("Pat_WS");
        meta.is_pattern_syntax = get_bool("Pat_Syn");
        meta.is_grapheme_base = get_bool("Gr_Base");
        meta.is_grapheme_extend = get_bool("Gr_Ext");
        meta.is_id_start = get_bool("IDS");
        meta.is_id_continue = get_bool("IDC");
        meta.is_xid_start = get_bool("XIDS");
        meta.is_xid_continue = get_bool("XIDC");
        meta.composition_exclusion = get_bool("CE");
        meta.full_composition_exclusion = get_bool("Comp_Ex");
        meta.changes_when_lowercased = get_bool("CWL");
        meta.changes_when_uppercased = get_bool("CWU");
        meta.changes_when_titlecased = get_bool("CWT");
        meta.changes_when_casefolded = get_bool("CWCF");
        meta.changes_when_casemapped = get_bool("CWCM");
        meta.changes_when_nfkc_casefolded = get_bool("CWKCF");
        meta.prepended_concatenation_mark = get_bool("PCM");
        meta.regional_indicator = get_bool("RI");
        meta.is_emoji = get_bool("Emoji");
        meta.is_emoji_presentation = get_bool("EPres");
        meta.is_emoji_modifier = get_bool("EMod");
        meta.is_emoji_modifier_base = get_bool("EBase");
        meta.is_emoji_component = get_bool("EComp");
        meta.is_extended_pictographic = get_bool("ExtPict");

        std::string krs = get_str("kRSUnicode");
        if (!krs.empty()) {
            size_t dot = krs.find('.');
            if (dot != std::string::npos) {
                try { meta.radical = std::stoul(krs.substr(0, dot)); } catch (...) {}
            } else {
                try { meta.radical = std::stoul(krs); } catch (...) {}
            }
        }
        std::string strokes_str = get_str("kTotalStrokes");
        if (strokes_str.empty()) strokes_str = get_str("Strokes");
        if (!strokes_str.empty()) {
            try { meta.strokes = std::stoi(strokes_str); } catch (...) {}
        }

        meta.nfc_quick_check = get_str("NFC_QC");
        meta.nfd_quick_check = get_str("NFD_QC");
        meta.nfkc_quick_check = get_str("NFKC_QC");
        meta.nfkd_quick_check = get_str("NFKD_QC");
        meta.nfkc_casefold = get_str("NFKC_CF");
        meta.nfkc_simple_casefold = get_str("NFKC_SCF");

        codepoints[cp] = std::move(meta);
    });

    db_.query("SELECT source_codepoints, primary_weight, secondary_weight, tertiary_weight FROM ucd.collation_weights", [&](const std::vector<std::string>& row) {
        std::string arr = row[0];
        if (arr.length() < 2) return;
        std::string content = arr.substr(1, arr.length() - 2);
        std::stringstream ss(content);
        std::string item;
        std::vector<uint32_t> cps;
        while (std::getline(ss, item, ',')) cps.push_back(std::stoul(item));

        if (cps.size() == 1 && codepoints.count(cps[0])) {
            UCAWeights w;
            w.primary = std::stoul(row[1]);
            w.secondary = std::stoul(row[2]);
            w.tertiary = std::stoul(row[3]);
            codepoints[cps[0]].uca_elements.push_back(w);
        }
    });
    std::cout << "  Loaded " << codepoints.size() << " codepoints from database." << std::endl;
}

void UCDProcessor::process_and_ingest() {
    using Clock = std::chrono::high_resolution_clock;
    auto pipeline_start = Clock::now();
    
    std::cout << "--- Starting Full Unicode Codespace Ingestion ---" << std::endl;
    
    auto step_start = Clock::now();
    std::cout << "[1/5] Parsing UCD files (XML + UCA + Confusables + Unihan + Emoji)..." << std::flush;
    parser_.parse_all();
    auto& codepoints = parser_.get_codepoints_mutable();
    auto step_duration = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - step_start).count();
    std::cout << " (" << step_duration << "ms)" << std::endl;

    step_start = Clock::now();
    std::cout << "[2/5] Semantic sequencing (" << codepoints.size() << " assigned codepoints)..." << std::flush;
    sequencer_.build_graph(codepoints);
    sorted_codepoints_ = sequencer_.linearize(codepoints);

    for (size_t i = 0; i < sorted_codepoints_.size(); ++i) {
        sorted_codepoints_[i]->sequence_index = static_cast<uint32_t>(i);
    }
    step_duration = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - step_start).count();
    std::cout << " (" << step_duration << "ms)" << std::endl;

    step_start = Clock::now();
    std::cout << "[3/5] Computing S³ positions for assigned codepoints..." << std::flush;
    size_t num_threads = std::thread::hardware_concurrency();
    size_t chunk_size = (sorted_codepoints_.size() + num_threads - 1) / num_threads;
    std::vector<std::thread> threads;
    for (size_t i = 0; i < num_threads; ++i) {
        size_t start = i * chunk_size;
        size_t end = std::min(start + chunk_size, sorted_codepoints_.size());
        if (start >= end) break;
        threads.emplace_back([this, start, end]() {
            for (size_t j = start; j < end; ++j) {
                auto* meta = sorted_codepoints_[j];
                meta->position = NodeGenerator::generate_node(meta->sequence_index, NodeGenerator::UNICODE_TOTAL);
            }
        });
    }
    for (auto& t : threads) t.join();
    step_duration = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - step_start).count();
    std::cout << " (" << step_duration << "ms)" << std::endl;

    step_start = Clock::now();
    std::cout << "[4/5] Ingesting " << sorted_codepoints_.size() << " assigned codepoints..." << std::flush;
    ingest_assigned_codepoints();
    step_duration = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - step_start).count();
    std::cout << " (" << step_duration << "ms)" << std::endl;
    
    step_start = Clock::now();
    std::cout << "[5/5] Streaming unassigned codepoints to complete codespace..." << std::flush;
    ingest_unassigned_codepoints();
    step_duration = std::chrono::duration_cast<std::chrono::seconds>(Clock::now() - step_start).count();
    std::cout << " (" << step_duration << "s)" << std::endl;
    
    auto total_duration = std::chrono::duration_cast<std::chrono::seconds>(Clock::now() - pipeline_start).count();
    std::cout << "✓ Full Unicode codespace seeded in " << total_duration << "s." << std::endl;
}

void UCDProcessor::ingest_assigned_codepoints() {
    PostgresConnection::Transaction txn(db_);
    const size_t n = sorted_codepoints_.size();
    std::vector<ComputedRow> rows(n);
    size_t num_threads = std::thread::hardware_concurrency();
    size_t chunk_size = (n + num_threads - 1) / num_threads;
    std::vector<std::thread> threads;
    for (size_t t = 0; t < num_threads; ++t) {
        size_t start = t * chunk_size;
        size_t end = std::min(start + chunk_size, n);
        if (start >= end) break;
        threads.emplace_back([this, &rows, start, end]() {
            for (size_t i = start; i < end; ++i) {
                auto* meta = sorted_codepoints_[i];
                ComputedRow& row = rows[i];
                auto atom_hash = BLAKE3Pipeline::hash_codepoint(meta->codepoint);
                format_uuid_to_string(atom_hash.data(), row.atom_uuid);

                std::vector<uint8_t> phys_data(sizeof(double) * 4);
                std::memcpy(phys_data.data(), meta->position.data(), sizeof(double) * 4);
                auto phys_hash = BLAKE3Pipeline::hash(phys_data);
                format_uuid_to_string(phys_hash.data(), row.phys_uuid);

                Eigen::Vector4d hypercube_coords;
                for (int k = 0; k < 4; ++k) hypercube_coords[k] = (meta->position[k] + 1.0) / 2.0;
                auto hilbert_idx = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords);
                format_uuid_to_string(hilbert_idx.data(), row.hilbert);
                geom_to_hex_fast(meta->position, row.centroid_hex);
                row.codepoint_str = std::to_string(meta->codepoint);
            }
        });
    }
    for (auto& t : threads) t.join();

    BulkCopy phys_copy(db_, false); 
    phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
    for (const auto& row : rows) phys_copy.add_row({row.phys_uuid, row.hilbert, row.centroid_hex});
    phys_copy.flush();

    BulkCopy atom_copy(db_, false);
    atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
    for (const auto& row : rows) atom_copy.add_row({row.atom_uuid, row.codepoint_str, row.phys_uuid});
    atom_copy.flush();
    txn.commit();
}

void UCDProcessor::ingest_unassigned_codepoints() {
    constexpr uint32_t MAX_CODEPOINT = 0x10FFFF;
    constexpr size_t BATCH_SIZE = 100000;
    std::bitset<MAX_CODEPOINT + 1> assigned_bits;
    for (auto* meta : sorted_codepoints_) assigned_bits.set(meta->codepoint);
    std::vector<uint32_t> unassigned_cps;
    unassigned_cps.reserve(MAX_CODEPOINT + 1 - sorted_codepoints_.size());
    for (uint32_t cp = 0; cp <= MAX_CODEPOINT; ++cp) if (!assigned_bits.test(cp)) unassigned_cps.push_back(cp);

    const size_t total = unassigned_cps.size();
    if (total == 0) return;
    uint32_t base_sequence = static_cast<uint32_t>(sorted_codepoints_.size());
    std::vector<ComputedRow> rows_buffer(BATCH_SIZE);
    size_t num_threads = std::thread::hardware_concurrency();

    for (size_t batch_start = 0; batch_start < total; batch_start += BATCH_SIZE) {
        PostgresConnection::Transaction txn(db_);
        size_t batch_end = std::min(batch_start + BATCH_SIZE, total);
        size_t current_batch_size = batch_end - batch_start;
        if (rows_buffer.size() != current_batch_size) rows_buffer.resize(current_batch_size);

        size_t chunk_size = (current_batch_size + num_threads - 1) / num_threads;
        std::vector<std::thread> threads;
        for (size_t t = 0; t < num_threads; ++t) {
            size_t start = t * chunk_size;
            size_t end = std::min(start + chunk_size, current_batch_size);
            if (start >= end) break;
            threads.emplace_back([&, start, end, batch_start, base_sequence]() {
                for (size_t i = start; i < end; ++i) {
                    uint32_t cp = unassigned_cps[batch_start + i];
                    uint32_t seq = base_sequence + static_cast<uint32_t>(batch_start + i);
                    ComputedRow& row = rows_buffer[i];
                    Eigen::Vector4d position = NodeGenerator::generate_node(seq, NodeGenerator::UNICODE_TOTAL);
                    std::vector<uint8_t> phys_data(sizeof(double) * 4);
                    std::memcpy(phys_data.data(), position.data(), sizeof(double) * 4);
                    auto phys_hash = BLAKE3Pipeline::hash(phys_data);
                    format_uuid_to_string(phys_hash.data(), row.phys_uuid);
                    Eigen::Vector4d hypercube_coords;
                    for (int k = 0; k < 4; ++k) hypercube_coords[k] = (position[k] + 1.0) / 2.0;
                    auto hilbert_idx = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords);
                    format_uuid_to_string(hilbert_idx.data(), row.hilbert);
                    geom_to_hex_fast(position, row.centroid_hex);
                    auto atom_hash = BLAKE3Pipeline::hash_codepoint(cp);
                    format_uuid_to_string(atom_hash.data(), row.atom_uuid);
                    row.codepoint_str = std::to_string(cp);
                }
            });
        }
        for (auto& t : threads) t.join();

        BulkCopy phys_copy(db_, false);
        phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
        for (const auto& row : rows_buffer) phys_copy.add_row({row.phys_uuid, row.hilbert, row.centroid_hex});
        phys_copy.flush();

        BulkCopy atom_copy(db_, false);
        atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
        for (const auto& row : rows_buffer) atom_copy.add_row({row.atom_uuid, row.codepoint_str, row.phys_uuid});
        atom_copy.flush();
        txn.commit();

        std::cout << "\r  Progress: " << ((batch_end * 100) / total) << "%" << std::flush;
    }
    std::cout << std::endl;
}

} // namespace Hartonomous::unicode
