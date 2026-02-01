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
#include <set>

extern "C" {
#include <liblwgeom.h>
}

namespace Hartonomous::unicode {

UCDProcessor::UCDProcessor(const std::string& data_dir, PostgresConnection& db)
    : parser_(data_dir), db_(db) {}

// Optimized Geometry Serialization
static std::string geom_to_hex(const Eigen::Vector4d& pt) {
    LWPOINT* lwpt = lwpoint_make4d(0, pt[0], pt[1], pt[2], pt[3]);
    FLAGS_SET_Z(lwpt->flags, 1);
    FLAGS_SET_M(lwpt->flags, 1);

    LWGEOM* geom = lwpoint_as_lwgeom(lwpt);
    lwgeom_set_srid(geom, 0);

    size_t size;
    GSERIALIZED* gser = gserialized_from_lwgeom(geom, &size);
    uint8_t* wkb = (uint8_t*)gser;

    std::ostringstream ss;
    ss << "\\\\x" << std::hex << std::setfill('0');
    for (size_t i = 0; i < size; ++i) {
        ss << std::setw(2) << static_cast<int>(wkb[i]);
    }

    lwgeom_free(geom);
    lwfree(gser);

    return ss.str();
}

static std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    static const char hex_chars[] = "0123456789abcdef";
    std::string uuid;
    uuid.reserve(36);
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) uuid += '-';
        uuid += hex_chars[(hash[i] >> 4) & 0xF];
        uuid += hex_chars[hash[i] & 0xF];
    }
    return uuid;
}

void UCDProcessor::process_and_ingest() {
    std::cout << "--- Starting Full Unicode Codespace Ingestion ---" << std::endl;

    // 1. Parsing (assigned codepoints only)
    std::cout << "[1/5] Parsing UCD files (assigned codepoints)..." << std::endl;
    parser_.parse_all();
    auto& codepoints = const_cast<std::map<uint32_t, CodepointMetadata>&>(parser_.get_codepoints());

    // 2. Semantic Sequencing for assigned codepoints
    std::cout << "[2/5] Semantic sequencing (" << codepoints.size() << " assigned codepoints)..." << std::endl;
    sequencer_.build_graph(codepoints);
    sorted_codepoints_ = sequencer_.linearize(codepoints);

    for (size_t i = 0; i < sorted_codepoints_.size(); ++i) {
        sorted_codepoints_[i]->sequence_index = static_cast<uint32_t>(i);
    }

    // 3. Generate S³ positions for assigned codepoints
    std::cout << "[3/5] Computing S³ positions for assigned codepoints..." << std::endl;
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

    // 4. Ingest assigned codepoints
    std::cout << "[4/5] Ingesting " << sorted_codepoints_.size() << " assigned codepoints..." << std::endl;
    ingest_assigned_codepoints();

    // 5. Stream unassigned codepoints (the remaining ~960k)
    std::cout << "[5/5] Streaming unassigned codepoints to complete codespace..." << std::endl;
    ingest_unassigned_codepoints();

    std::cout << "✓ Full Unicode codespace seeded (1,114,112 codepoints)." << std::endl;
}

void UCDProcessor::ingest_assigned_codepoints() {
    struct IngestRow {
        std::vector<std::string> phys_cols;
        std::vector<std::string> atom_cols;
    };

    // Collect all rows
    std::vector<IngestRow> all_rows;
    all_rows.reserve(sorted_codepoints_.size());

    for (auto* meta : sorted_codepoints_) {
        std::vector<uint8_t> phys_data(4 * sizeof(double));
        std::memcpy(phys_data.data(), meta->position.data(), 4 * sizeof(double));
        auto phys_hash = BLAKE3Pipeline::hash(phys_data);
        std::string phys_uuid = hash_to_uuid(phys_hash);

        Eigen::Vector4d hypercube_coords;
        for (int k = 0; k < 4; ++k) hypercube_coords[k] = (meta->position[k] + 1.0) / 2.0;
        auto h_index = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords);

        IngestRow row;
        row.phys_cols = {phys_uuid, h_index.to_string(), geom_to_hex(meta->position)};

        auto atom_hash = BLAKE3Pipeline::hash_codepoint(meta->codepoint);
        row.atom_cols = {hash_to_uuid(atom_hash), std::to_string(meta->codepoint), phys_uuid};

        all_rows.push_back(std::move(row));
    }

    // Ingest physicality first
    {
        BulkCopy phys_copy(db_);
        phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
        for (const auto& row : all_rows) {
            phys_copy.add_row(row.phys_cols);
        }
        phys_copy.flush();
    }

    // Then atoms
    {
        BulkCopy atom_copy(db_);
        atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
        for (const auto& row : all_rows) {
            atom_copy.add_row(row.atom_cols);
        }
        atom_copy.flush();
    }

    std::cout << "  Inserted " << all_rows.size() << " assigned codepoints." << std::endl;
}

void UCDProcessor::ingest_unassigned_codepoints() {
    // Build set of assigned codepoints for fast lookup
    std::set<uint32_t> assigned_cps;
    for (auto* meta : sorted_codepoints_) {
        assigned_cps.insert(meta->codepoint);
    }

    constexpr uint32_t MAX_CODEPOINT = 0x10FFFF;
    constexpr size_t BATCH_SIZE = 50000;

    // Sequence index for unassigned continues after assigned
    uint32_t next_sequence = static_cast<uint32_t>(sorted_codepoints_.size());

    size_t unassigned_count = 0;
    size_t total_unassigned = MAX_CODEPOINT + 1 - assigned_cps.size();

    std::vector<std::vector<std::string>> phys_rows;
    std::vector<std::vector<std::string>> atom_rows;
    phys_rows.reserve(BATCH_SIZE);
    atom_rows.reserve(BATCH_SIZE);

    auto flush_batch = [&]() {
        if (phys_rows.empty()) return;

        // Insert physicality
        {
            BulkCopy phys_copy(db_);
            phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
            for (const auto& row : phys_rows) {
                phys_copy.add_row(row);
            }
            phys_copy.flush();
        }

        // Insert atoms
        {
            BulkCopy atom_copy(db_);
            atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
            for (const auto& row : atom_rows) {
                atom_copy.add_row(row);
            }
            atom_copy.flush();
        }

        phys_rows.clear();
        atom_rows.clear();
    };

    for (uint32_t cp = 0; cp <= MAX_CODEPOINT; ++cp) {
        if (assigned_cps.count(cp)) continue;

        // Generate S³ position for this unassigned codepoint
        Eigen::Vector4d position = NodeGenerator::generate_node(next_sequence++);

        // Compute physicality hash
        std::vector<uint8_t> phys_data(4 * sizeof(double));
        std::memcpy(phys_data.data(), position.data(), 4 * sizeof(double));
        auto phys_hash = BLAKE3Pipeline::hash(phys_data);
        std::string phys_uuid = hash_to_uuid(phys_hash);

        // Compute Hilbert index
        Eigen::Vector4d hypercube_coords;
        for (int k = 0; k < 4; ++k) hypercube_coords[k] = (position[k] + 1.0) / 2.0;
        auto h_index = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords);

        // Compute atom hash
        auto atom_hash = BLAKE3Pipeline::hash_codepoint(cp);

        phys_rows.push_back({phys_uuid, h_index.to_string(), geom_to_hex(position)});
        atom_rows.push_back({hash_to_uuid(atom_hash), std::to_string(cp), phys_uuid});

        ++unassigned_count;

        if (phys_rows.size() >= BATCH_SIZE) {
            flush_batch();
            size_t percent = (unassigned_count * 100) / total_unassigned;
            std::cout << "\r  Progress: " << percent << "% (" << unassigned_count << "/" << total_unassigned << ")" << std::flush;
        }
    }

    flush_batch();
    std::cout << "\r  Inserted " << unassigned_count << " unassigned codepoints.        " << std::endl;
}

} // namespace Hartonomous::unicode
