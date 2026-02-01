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
    std::cout << "--- Starting Vertically Integrated UCD Ingestion ---" << std::endl;
    
    // 1. Parsing
    std::cout << "[1/4] Parsing UCD Universe..." << std::endl;
    parser_.parse_all();
    auto& codepoints = const_cast<std::vector<CodepointMetadata>&>(parser_.get_codepoints());

    // 2. Semantic Sequencing
    std::cout << "[2/4] Executing Semantic Sequencing (1D Backbone)..." << std::endl;
    sequencer_.build_graph(codepoints);
    sorted_codepoints_ = sequencer_.linearize(codepoints);
    
    for (size_t i = 0; i < sorted_codepoints_.size(); ++i) {
        sorted_codepoints_[i]->sequence_index = static_cast<uint32_t>(i);
    }

    // 3. Parallel Geometry Mapping
    std::cout << "[3/4] Mapping Sequence to S3 + Hilbert (Parallel)..." << std::endl;
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

    // 4. Optimized DB Ingestion
    std::cout << "[4/4] Bulk Ingesting to Physicality & Atom tables..." << std::endl;
    ingest_to_db();
    
    std::cout << "✓ Unicode Universe Seeding Successful." << std::endl;
}

void UCDProcessor::ingest_to_db() {
    struct IngestRow {
        std::vector<std::string> phys_cols;
        std::vector<std::string> atom_cols;
    };

    size_t num_threads = std::thread::hardware_concurrency();
    size_t chunk_size = (sorted_codepoints_.size() + num_threads - 1) / num_threads;
    
    std::vector<std::future<std::vector<IngestRow>>> futures;

    for (size_t i = 0; i < num_threads; ++i) {
        size_t start = i * chunk_size;
        size_t end = std::min(start + chunk_size, sorted_codepoints_.size());
        if (start >= end) break;

        futures.push_back(std::async(std::launch::async, [this, start, end]() {
            std::vector<IngestRow> rows;
            rows.reserve(end - start);
            for (size_t j = start; j < end; ++j) {
                auto* meta = sorted_codepoints_[j];
                
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
                
                rows.push_back(std::move(row));
            }
            return rows;
        }));
    }

    // Collect all rows first (parallel computation already done via futures)
    std::vector<IngestRow> all_rows;
    size_t total_rows_processed = 0;
    size_t total_expected = sorted_codepoints_.size();
    
    std::cout << "  Generating rows (parallel)... " << std::flush;
    for (auto& f : futures) {
        auto rows = f.get();
        all_rows.insert(all_rows.end(),
                        std::make_move_iterator(rows.begin()),
                        std::make_move_iterator(rows.end()));
        total_rows_processed += rows.size();
        
        // Simple progress indicator
        size_t percent = (total_rows_processed * 100) / total_expected;
        if (percent % 10 == 0) std::cout << percent << "%... " << std::flush;
    }
    std::cout << "Done. (" << all_rows.size() << " rows)" << std::endl;

    // Ingest physicality first (atoms depend on physicality via FK)
    {
        std::cout << "  Bulk inserting Physicality records... " << std::flush;
        BulkCopy phys_copy(db_);
        phys_copy.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
        size_t count = 0;
        for (const auto& row : all_rows) {
            phys_copy.add_row(row.phys_cols);
            if (++count % (all_rows.size() / 10) == 0) std::cout << "." << std::flush;
        }
        phys_copy.flush();
        std::cout << " Done." << std::endl;
    }

    // Then ingest atoms
    {
        std::cout << "  Bulk inserting Atom records... " << std::flush;
        BulkCopy atom_copy(db_);
        atom_copy.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
        size_t count = 0;
        for (const auto& row : all_rows) {
            atom_copy.add_row(row.atom_cols);
            if (++count % (all_rows.size() / 10) == 0) std::cout << "." << std::flush;
        }
        atom_copy.flush();
        std::cout << " Done." << std::endl;
    }
}

} // namespace Hartonomous::unicode
