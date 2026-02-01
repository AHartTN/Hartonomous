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

// Pre-computed hex lookup table for faster conversion
static const char* HEX_TABLE = "0123456789abcdef";

// Fast UUID formatting from 16-byte hash
static void format_uuid(const uint8_t* hash, char* out) {
    int j = 0;
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) out[j++] = '-';
        out[j++] = HEX_TABLE[(hash[i] >> 4) & 0xF];
        out[j++] = HEX_TABLE[hash[i] & 0xF];
    }
    out[j] = '\0';
}

// Ultra-fast geometry serialization - write EWKB directly, no liblwgeom!
// EWKB PointZM format (SRID=0):
// - 1 byte:  byte order (0x01 = little endian)
// - 4 bytes: type (0xC0000001 = Point with Z and M, little endian)
// - 32 bytes: X, Y, Z, M as little-endian doubles
// Total: 37 bytes
static void geom_to_hex_fast(const Eigen::Vector4d& pt, std::string& out) {
    // EWKB header: byte order + type
    static constexpr uint8_t EWKB_HEADER[5] = {
        0x01,                   // Little endian
        0x01, 0x00, 0x00, 0xC0  // PointZM type (0xC0000001 in LE)
    };

    // Pre-size output: "\\x" + 2 chars per byte * 37 bytes = 78 chars
    out.clear();
    out.reserve(78);
    out = "\\\\x";

    // Write header
    for (int i = 0; i < 5; ++i) {
        out += HEX_TABLE[(EWKB_HEADER[i] >> 4) & 0xF];
        out += HEX_TABLE[EWKB_HEADER[i] & 0xF];
    }

    // Write X, Y, Z, M as little-endian doubles
    const double coords[4] = {pt[0], pt[1], pt[2], pt[3]};
    for (int c = 0; c < 4; ++c) {
        const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&coords[c]);
        for (int i = 0; i < 8; ++i) {
            out += HEX_TABLE[(bytes[i] >> 4) & 0xF];
            out += HEX_TABLE[bytes[i] & 0xF];
        }
    }
}

// Struct for pre-computed row data (thread-safe)
struct ComputedRow {
    std::string phys_uuid;
    std::string hilbert;
    std::string centroid_hex;
    std::string atom_uuid;
    std::string codepoint_str;  // Pre-computed string for DB insert
};

void UCDProcessor::process_and_ingest() {
    std::cout << "--- Starting Full Unicode Codespace Ingestion ---" << std::endl;

    // 1. Parsing (assigned codepoints only)
    std::cout << "[1/5] Parsing UCD files (assigned codepoints)..." << std::endl;
    parser_.parse_all();
    auto& codepoints = parser_.get_codepoints_mutable();

    // 2. Semantic Sequencing for assigned codepoints
    std::cout << "[2/5] Semantic sequencing (" << codepoints.size() << " assigned codepoints)..." << std::endl;
    sequencer_.build_graph(codepoints);
    sorted_codepoints_ = sequencer_.linearize(codepoints);

    for (size_t i = 0; i < sorted_codepoints_.size(); ++i) {
        sorted_codepoints_[i]->sequence_index = static_cast<uint32_t>(i);
    }

    // 3. Generate S³ positions for assigned codepoints (parallel)
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
    const size_t n = sorted_codepoints_.size();
    std::vector<ComputedRow> rows(n);

    // Parallel computation of all row data
    size_t num_threads = std::thread::hardware_concurrency();
    size_t chunk_size = (n + num_threads - 1) / num_threads;

    std::vector<std::thread> threads;
    for (size_t t = 0; t < num_threads; ++t) {
        size_t start = t * chunk_size;
        size_t end = std::min(start + chunk_size, n);
        if (start >= end) break;

        threads.emplace_back([this, &rows, start, end]() {
            char uuid_buf[37];
            for (size_t i = start; i < end; ++i) {
                auto* meta = sorted_codepoints_[i];
                ComputedRow& row = rows[i];

                // Physicality hash from position
                std::vector<uint8_t> phys_data(4 * sizeof(double));
                std::memcpy(phys_data.data(), meta->position.data(), 4 * sizeof(double));
                auto phys_hash = BLAKE3Pipeline::hash(phys_data);
                format_uuid(phys_hash.data(), uuid_buf);
                row.phys_uuid = uuid_buf;

                // Hilbert index
                Eigen::Vector4d hypercube_coords;
                for (int k = 0; k < 4; ++k) hypercube_coords[k] = (meta->position[k] + 1.0) / 2.0;
                auto h_index = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords);
                row.hilbert = h_index.to_string();

                // Geometry hex
                geom_to_hex_fast(meta->position, row.centroid_hex);

                // Atom hash
                auto atom_hash = BLAKE3Pipeline::hash_codepoint(meta->codepoint);
                format_uuid(atom_hash.data(), uuid_buf);
                row.atom_uuid = uuid_buf;

                row.codepoint_str = std::to_string(meta->codepoint);
            }
        });
    }
    for (auto& t : threads) t.join();

    // Sequential DB insert (single connection)
    {
        BulkCopy phys_copy(db_, false);  // Direct COPY, no temp table
        phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
        for (const auto& row : rows) {
            phys_copy.add_row({row.phys_uuid, row.hilbert, row.centroid_hex});
        }
        phys_copy.flush();
    }

    {
        BulkCopy atom_copy(db_, false);  // Direct COPY, no temp table
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
    constexpr size_t BATCH_SIZE = 100000;  // Larger batches

    // Use bitset for O(1) lookup - much faster than std::set
    std::bitset<MAX_CODEPOINT + 1> assigned_bits;
    for (auto* meta : sorted_codepoints_) {
        assigned_bits.set(meta->codepoint);
    }

    // Build list of unassigned codepoints
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

    // Process in parallel batches
    size_t num_threads = std::thread::hardware_concurrency();

    for (size_t batch_start = 0; batch_start < total; batch_start += BATCH_SIZE) {
        size_t batch_end = std::min(batch_start + BATCH_SIZE, total);
        size_t batch_size = batch_end - batch_start;

        std::vector<ComputedRow> rows(batch_size);

        // Parallel computation within batch
        size_t chunk_size = (batch_size + num_threads - 1) / num_threads;
        std::vector<std::thread> threads;

        for (size_t t = 0; t < num_threads; ++t) {
            size_t start = t * chunk_size;
            size_t end = std::min(start + chunk_size, batch_size);
            if (start >= end) break;

            threads.emplace_back([&, start, end, batch_start, base_sequence]() {
                char uuid_buf[37];
                for (size_t i = start; i < end; ++i) {
                    uint32_t cp = unassigned_cps[batch_start + i];
                    uint32_t seq = base_sequence + static_cast<uint32_t>(batch_start + i);
                    ComputedRow& row = rows[i];

                    // Generate position
                    Eigen::Vector4d position = NodeGenerator::generate_node(seq);

                    // Physicality hash
                    std::vector<uint8_t> phys_data(4 * sizeof(double));
                    std::memcpy(phys_data.data(), position.data(), 4 * sizeof(double));
                    auto phys_hash = BLAKE3Pipeline::hash(phys_data);
                    format_uuid(phys_hash.data(), uuid_buf);
                    row.phys_uuid = uuid_buf;

                    // Hilbert index
                    Eigen::Vector4d hypercube_coords;
                    for (int k = 0; k < 4; ++k) hypercube_coords[k] = (position[k] + 1.0) / 2.0;
                    auto h_index = hartonomous::spatial::HilbertCurve4D::encode(hypercube_coords);
                    row.hilbert = h_index.to_string();

                    // Geometry hex
                    geom_to_hex_fast(position, row.centroid_hex);

                    // Atom hash
                    auto atom_hash = BLAKE3Pipeline::hash_codepoint(cp);
                    format_uuid(atom_hash.data(), uuid_buf);
                    row.atom_uuid = uuid_buf;

                    row.codepoint_str = std::to_string(cp);
                }
            });
        }
        for (auto& t : threads) t.join();

        // Sequential DB insert
        {
            BulkCopy phys_copy(db_, false);  // Direct COPY, no temp table
            phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
            for (const auto& row : rows) {
                phys_copy.add_row({row.phys_uuid, row.hilbert, row.centroid_hex});
            }
            phys_copy.flush();
        }

        {
            BulkCopy atom_copy(db_, false);  // Direct COPY, no temp table
            atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
            for (const auto& row : rows) {
                atom_copy.add_row({row.atom_uuid, row.codepoint_str, row.phys_uuid});
            }
            atom_copy.flush();
        }

        size_t percent = ((batch_end) * 100) / total;
        std::cout << "\r  Progress: " << percent << "% (" << batch_end << "/" << total << ")" << std::flush;
    }

    std::cout << "\r  Inserted " << total << " unassigned codepoints.              " << std::endl;
}

} // namespace Hartonomous::unicode
