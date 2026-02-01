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

namespace Hartonomous::unicode {

UCDProcessor::UCDProcessor(const std::string& data_dir, PostgresConnection& db)
    : parser_(data_dir), db_(db) {}

static const char* HEX_TABLE = "0123456789abcdef";

// Optimized: writes directly to existing string buffer to avoid allocation if capacity exists
static void format_uuid_to_string(const uint8_t* hash, std::string& out) {
    out.clear(); // Keeps capacity
    out.reserve(36); 
    // Manual unroll or loop
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) out.push_back('-');
        out.push_back(HEX_TABLE[(hash[i] >> 4) & 0xF]);
        out.push_back(HEX_TABLE[hash[i] & 0xF]);
    }
}

// Optimized: writes directly to existing string buffer
// Outputs raw EWKB hex for GEOMETRY columns (no \x prefix - that's for bytea)
static void geom_to_hex_fast(const Eigen::Vector4d& pt, std::string& out) {
    // EWKB header: byte order + type
    static constexpr uint8_t EWKB_HEADER[5] = {
        0x01,                   // Little endian
        0x01, 0x00, 0x00, 0xC0  // PointZM type (0xC0000001)
    };

    out.clear();
    out.reserve(74); // 5 header bytes + 32 coord bytes = 37 bytes * 2 hex chars = 74

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

void UCDProcessor::process_and_ingest() {
    // [Keep existing implementation for steps 1-3]
    std::cout << "--- Starting Full Unicode Codespace Ingestion ---" << std::endl;
    std::cout << "[1/6] Parsing UCD files (full XML + supplementary)..." << std::endl;
    parser_.parse_all();
    auto& codepoints = parser_.get_codepoints_mutable();

    std::cout << "[2/6] Semantic sequencing (" << codepoints.size() << " assigned codepoints)..." << std::endl;
    sequencer_.build_graph(codepoints);
    sorted_codepoints_ = sequencer_.linearize(codepoints);

    for (size_t i = 0; i < sorted_codepoints_.size(); ++i) {
        sorted_codepoints_[i]->sequence_index = static_cast<uint32_t>(i);
    }

    std::cout << "[3/6] Computing S³ positions for assigned codepoints..." << std::endl;
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
                meta->position = NodeGenerator::generate_node(meta->sequence_index);
            }
        });
    }
    for (auto& t : threads) t.join();

    std::cout << "[4/6] Ingesting " << sorted_codepoints_.size() << " assigned codepoints..." << std::endl;
    ingest_assigned_codepoints();

    std::cout << "[5/6] Streaming unassigned codepoints to complete codespace..." << std::endl;
    ingest_unassigned_codepoints();

    // Ingest full UCD metadata from XML
    ingest_ucd_metadata();

    std::cout << "✓ Full Unicode codespace seeded (1,114,112 codepoints + " << parser_.codepoint_count() << " metadata records)." << std::endl;
}

void UCDProcessor::ingest_assigned_codepoints() {
    // [Optimization: Reuse logic or keep as is since it runs once]
    // The main bottleneck is unassigned, but we apply similar string optimizations here.
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

                std::vector<uint8_t> phys_data(32); // 4 * sizeof(double)
                std::memcpy(phys_data.data(), meta->position.data(), 32);
                auto phys_hash = BLAKE3Pipeline::hash(phys_data);
                format_uuid_to_string(phys_hash.data(), row.phys_uuid);

                Eigen::Vector4d hypercube_coords;
                for (int k = 0; k < 4; ++k) hypercube_coords[k] = (meta->position[k] + 1.0) / 2.0;
                row.hilbert = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords).to_string();

                geom_to_hex_fast(meta->position, row.centroid_hex);

                auto atom_hash = BLAKE3Pipeline::hash_codepoint(meta->codepoint);
                format_uuid_to_string(atom_hash.data(), row.atom_uuid);

                row.codepoint_str = std::to_string(meta->codepoint);
            }
        });
    }
    for (auto& t : threads) t.join();

    {
        BulkCopy phys_copy(db_, false); 
        phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
        for (const auto& row : rows) {
            phys_copy.add_row({row.phys_uuid, row.hilbert, row.centroid_hex});
        }
        phys_copy.flush();
    }
    {
        BulkCopy atom_copy(db_, false);
        atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
        for (const auto& row : rows) {
            atom_copy.add_row({row.atom_uuid, row.codepoint_str, row.phys_uuid});
        }
        atom_copy.flush();
    }
    std::cout << "  Inserted " << rows.size() << " assigned codepoints." << std::endl;
}

void UCDProcessor::ingest_unassigned_codepoints() {
    constexpr uint32_t MAX_CODEPOINT = 0x10FFFF;
    constexpr size_t BATCH_SIZE = 100000;

    std::bitset<MAX_CODEPOINT + 1> assigned_bits;
    for (auto* meta : sorted_codepoints_) {
        assigned_bits.set(meta->codepoint);
    }

    std::vector<uint32_t> unassigned_cps;
    unassigned_cps.reserve(MAX_CODEPOINT + 1 - sorted_codepoints_.size());
    for (uint32_t cp = 0; cp <= MAX_CODEPOINT; ++cp) {
        if (!assigned_bits.test(cp)) {
            unassigned_cps.push_back(cp);
        }
    }

    const size_t total = unassigned_cps.size();
    uint32_t base_sequence = static_cast<uint32_t>(sorted_codepoints_.size());

    std::cout << "  Processing " << total << " unassigned codepoints..." << std::endl;

    // CRITICAL OPTIMIZATION: 
    // Allocate rows buffer ONCE outside the loop.
    // std::string members will retain capacity between batches.
    // This saves ~1 million malloc/free pairs.
    std::vector<ComputedRow> rows_buffer(BATCH_SIZE);

    size_t num_threads = std::thread::hardware_concurrency();

    for (size_t batch_start = 0; batch_start < total; batch_start += BATCH_SIZE) {
        size_t batch_end = std::min(batch_start + BATCH_SIZE, total);
        size_t current_batch_size = batch_end - batch_start;

        // Resize logically, but capacity remains from initial BATCH_SIZE allocation
        if (rows_buffer.size() != current_batch_size) {
            rows_buffer.resize(current_batch_size);
        }

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
                    
                    // Access existing row object in buffer
                    ComputedRow& row = rows_buffer[i];

                    Eigen::Vector4d position = NodeGenerator::generate_node(seq);

                    std::vector<uint8_t> phys_data(32);
                    std::memcpy(phys_data.data(), position.data(), 32);
                    auto phys_hash = BLAKE3Pipeline::hash(phys_data);
                    
                    // Writes to existing buffer capacity
                    format_uuid_to_string(phys_hash.data(), row.phys_uuid);

                    Eigen::Vector4d hypercube_coords;
                    for (int k = 0; k < 4; ++k) hypercube_coords[k] = (position[k] + 1.0) / 2.0;
                    
                    // Hilbert currently returns a new string, could optimize hilbert class later
                    row.hilbert = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords).to_string();

                    // Writes to existing buffer capacity
                    geom_to_hex_fast(position, row.centroid_hex);

                    auto atom_hash = BLAKE3Pipeline::hash_codepoint(cp);
                    format_uuid_to_string(atom_hash.data(), row.atom_uuid);

                    row.codepoint_str = std::to_string(cp);
                }
            });
        }
        for (auto& t : threads) t.join();

        // Sequential DB insert
        {
            BulkCopy phys_copy(db_, false);
            phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
            for (const auto& row : rows_buffer) {
                phys_copy.add_row({row.phys_uuid, row.hilbert, row.centroid_hex});
            }
            phys_copy.flush();
        }

        {
            BulkCopy atom_copy(db_, false);
            atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
            for (const auto& row : rows_buffer) {
                atom_copy.add_row({row.atom_uuid, row.codepoint_str, row.phys_uuid});
            }
            atom_copy.flush();
        }

        size_t percent = ((batch_end) * 100) / total;
        std::cout << "\r  Progress: " << percent << "% (" << batch_end << "/" << total << ")" << std::flush;
    }

    std::cout << "\r  Inserted " << total << " unassigned codepoints.              " << std::endl;
}

void UCDProcessor::ingest_ucd_metadata() {
    std::cout << "[6/6] Ingesting full UCD metadata..." << std::endl;

    const auto& codepoints = parser_.get_codepoints();
    std::vector<const CodepointMetadata*> all_metadata;
    all_metadata.reserve(codepoints.size());

    for (const auto& [cp, meta] : codepoints) {
        all_metadata.push_back(&meta);
    }

    constexpr size_t BATCH_SIZE = 10000;
    size_t total = all_metadata.size();

    for (size_t batch_start = 0; batch_start < total; batch_start += BATCH_SIZE) {
        size_t batch_end = std::min(batch_start + BATCH_SIZE, total);

        std::ostringstream sql;
        sql << "INSERT INTO hartonomous.atommetadata (atomid, codepoint, name, name1, "
            << "generalcategory, combiningclass, script, scriptextensions, block, age, "
            << "decompositiontype, decompositionmapping, uppercasemapping, lowercasemapping, "
            << "titlecasemapping, simplecasefolding, casefolding, numerictype, numericvalue, "
            << "bidiclass, bidimirrored, bidimirroringglyph, bidicontrol, "
            << "joiningtype, joininggroup, joincontrol, eastasianwidth, linebreak, "
            << "wordbreak, sentencebreak, graphemeclusterbreak, hangulsyllabletype, "
            << "isalphabetic, isuppercase, islowercase, iscased, ismath, ishexdigit, "
            << "isideographic, isunifiedideograph, isradical, isdash, iswhitespace, "
            << "isquotationmark, isterminalpunctuation, issentenceterminal, isdiacritic, "
            << "isextender, issoftdotted, isdeprecated, isdefaultignorable, "
            << "isvariationselector, isnoncharacter, ispatternwhitespace, ispatternsyntax, "
            << "isgraphemebase, isgraphemeextend, isidstart, isidcontinue, isxidstart, isxidcontinue, "
            << "compositionexclusion, fullcompositionexclusion, "
            << "isemoji, isemojipresentation, isemojimodifier, isemojimodifierbase, "
            << "isemojicomponent, isextendedpictographic, radical, strokes, "
            << "nfcquickcheck, nfdquickcheck, nfkcquickcheck, nfkdquickcheck) VALUES ";

        bool first = true;
        for (size_t i = batch_start; i < batch_end; ++i) {
            const CodepointMetadata* m = all_metadata[i];

            // Get atom UUID from codepoint
            auto atom_hash = BLAKE3Pipeline::hash_codepoint(m->codepoint);
            std::string atom_uuid;
            format_uuid_to_string(atom_hash.data(), atom_uuid);

            if (!first) sql << ",";
            first = false;

            auto escape = [](const std::string& s) -> std::string {
                std::string result;
                for (char c : s) {
                    if (c == '\'') result += "''";
                    else result += c;
                }
                return result;
            };

            auto quote = [&escape](const std::string& s) -> std::string {
                if (s.empty()) return "NULL";
                return "'" + escape(s) + "'";
            };

            auto bool_str = [](bool b) -> const char* { return b ? "TRUE" : "FALSE"; };

            sql << "('" << atom_uuid << "'," << m->codepoint << ","
                << quote(m->name) << "," << quote(m->name1) << ","
                << quote(m->general_category) << "," << (int)m->combining_class << ","
                << quote(m->script) << "," << quote(m->script_extensions) << ","
                << quote(m->block) << "," << quote(m->age) << ","
                << quote(m->decomposition_type) << "," << quote(m->decomposition_mapping) << ","
                << quote(m->uppercase_mapping) << "," << quote(m->lowercase_mapping) << ","
                << quote(m->titlecase_mapping) << "," << quote(m->simple_case_folding) << ","
                << quote(m->case_folding) << "," << quote(m->numeric_type) << ","
                << quote(m->numeric_value) << "," << quote(m->bidi_class) << ","
                << bool_str(m->bidi_mirrored) << "," << quote(m->bidi_mirroring_glyph) << ","
                << bool_str(m->bidi_control) << "," << quote(m->joining_type) << ","
                << quote(m->joining_group) << "," << bool_str(m->join_control) << ","
                << quote(m->east_asian_width) << "," << quote(m->line_break) << ","
                << quote(m->word_break) << "," << quote(m->sentence_break) << ","
                << quote(m->grapheme_cluster_break) << "," << quote(m->hangul_syllable_type) << ","
                << bool_str(m->is_alphabetic) << "," << bool_str(m->is_uppercase) << ","
                << bool_str(m->is_lowercase) << "," << bool_str(m->is_cased) << ","
                << bool_str(m->is_math) << "," << bool_str(m->is_hex_digit) << ","
                << bool_str(m->is_ideographic) << "," << bool_str(m->is_unified_ideograph) << ","
                << bool_str(m->is_radical) << "," << bool_str(m->is_dash) << ","
                << bool_str(m->is_whitespace) << "," << bool_str(m->is_quotation_mark) << ","
                << bool_str(m->is_terminal_punctuation) << "," << bool_str(m->is_sentence_terminal) << ","
                << bool_str(m->is_diacritic) << "," << bool_str(m->is_extender) << ","
                << bool_str(m->is_soft_dotted) << "," << bool_str(m->is_deprecated) << ","
                << bool_str(m->is_default_ignorable) << "," << bool_str(m->is_variation_selector) << ","
                << bool_str(m->is_noncharacter) << "," << bool_str(m->is_pattern_whitespace) << ","
                << bool_str(m->is_pattern_syntax) << "," << bool_str(m->is_grapheme_base) << ","
                << bool_str(m->is_grapheme_extend) << "," << bool_str(m->is_id_start) << ","
                << bool_str(m->is_id_continue) << "," << bool_str(m->is_xid_start) << ","
                << bool_str(m->is_xid_continue) << "," << bool_str(m->composition_exclusion) << ","
                << bool_str(m->full_composition_exclusion) << "," << bool_str(m->is_emoji) << ","
                << bool_str(m->is_emoji_presentation) << "," << bool_str(m->is_emoji_modifier) << ","
                << bool_str(m->is_emoji_modifier_base) << "," << bool_str(m->is_emoji_component) << ","
                << bool_str(m->is_extended_pictographic) << ","
                << (m->radical > 0 ? std::to_string(m->radical) : "NULL") << ","
                << (m->strokes != 0 ? std::to_string(m->strokes) : "NULL") << ","
                << quote(m->nfc_quick_check) << "," << quote(m->nfd_quick_check) << ","
                << quote(m->nfkc_quick_check) << "," << quote(m->nfkd_quick_check) << ")";
        }

        sql << " ON CONFLICT (atomid) DO UPDATE SET modifiedat = CURRENT_TIMESTAMP";
        db_.execute(sql.str());

        size_t percent = (batch_end * 100) / total;
        std::cout << "\r  Progress: " << percent << "% (" << batch_end << "/" << total << ")" << std::flush;
    }

    std::cout << "\r  Inserted " << total << " UCD metadata records.              " << std::endl;
}

} // namespace Hartonomous::unicode