#include <unicode/ingestor/ucd_processor.hpp>
#include <unicode/ingestor/node_generator.hpp>
#include <unicode/ingestor/semantic_sequencer.hpp>
#include <storage/physicality_store.hpp>
#include <storage/atom_store.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <utils/time.hpp>
#include <iostream>
#include <algorithm>
#include <thread>
#include <bitset> // Added missing header
#include <cstring>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

namespace Hartonomous::unicode {

UCDProcessor::UCDProcessor(const std::string& data_dir, PostgresConnection& db)
    : parser_(data_dir), db_(db) {}

void UCDProcessor::process_and_ingest() {
    Timer pipeline_timer;
    std::cout << "--- Rigorous Unicode Seeding Sequence ---" << std::endl;
    
    std::cout << "[1/5] Parsing UCD XML and DUCET Collation..." << std::flush;
    Timer t; parser_.parse_all();
    auto& codepoints = parser_.get_codepoints_mutable();
    std::cout << " (" << t.elapsed_ms() << "ms)" << std::endl;

    std::cout << "[2/5] Recording Gene Pool Metadata..." << std::flush;
    t.reset(); ingest_metadata();
    std::cout << " (" << t.elapsed_ms() << "ms)" << std::endl;

    std::cout << "[3/5] UCA-driven Semantic Sequencing..." << std::flush;
    t.reset(); sequencer_.build_graph(codepoints);
    sorted_codepoints_ = sequencer_.linearize(codepoints);
    for (size_t i = 0; i < sorted_codepoints_.size(); ++i) 
        sorted_codepoints_[i]->sequence_index = static_cast<uint32_t>(i);
    std::cout << " (" << t.elapsed_ms() << "ms)" << std::endl;

    std::cout << "[4/5] Projecting Atoms onto S3 Hypersphere..." << std::flush;
    t.reset();
    #pragma omp parallel for schedule(dynamic, 1024)
    for (size_t j = 0; j < sorted_codepoints_.size(); ++j) {
        sorted_codepoints_[j]->position = NodeGenerator::generate_node(sorted_codepoints_[j]->sequence_index, NodeGenerator::UNICODE_TOTAL);
    }
    std::cout << " (" << t.elapsed_ms() << "ms)" << std::endl;

    std::cout << "[5/5] Finalizing Atoms + Physicality..." << std::flush;
    t.reset();
    ingest_assigned_codepoints();
    ingest_unassigned_codepoints();
    std::cout << " (" << t.elapsed_sec() << "s)" << std::endl;
    
    std::cout << "✓ Unicode seeding complete in " << pipeline_timer.elapsed_sec() << "s." << std::endl;
}

void UCDProcessor::ingest_metadata() {
    db_.execute("TRUNCATE TABLE ucd.code_points, ucd.collation_weights CASCADE");
    auto& codepoints = parser_.get_codepoints_mutable();
    PostgresConnection::Transaction txn(db_);
    
    BulkCopy ucd_copy(db_, false);
    ucd_copy.begin_table("ucd.code_points", {"codepoint", "name", "generalcategory", "block", "age", "script", "properties", "basecodepoint", "radical", "strokes"});
    
    for (const auto& [cp, meta] : codepoints) {
        json props;
        props["na1"] = meta.name1;
        props["ccc"] = meta.combining_class;
        props["scx"] = meta.script_extensions;
        props["dm"] = meta.decomposition_mapping;
        props["Upper"] = meta.is_uppercase;
        props["Lower"] = meta.is_lowercase;
        props["Emoji"] = meta.is_emoji;

        ucd_copy.add_row({
            std::to_string(cp), meta.name, meta.general_category, meta.block, meta.age, meta.script,
            props.dump(), std::to_string(meta.base_codepoint), std::to_string(meta.radical), std::to_string(meta.strokes)
        });
    }
    ucd_copy.flush();

    BulkCopy uca_copy(db_, false);
    uca_copy.begin_table("ucd.collation_weights", {"sourcecodepoints", "primaryweight", "secondaryweight", "tertiaryweight"});
    for (const auto& [cp, meta] : codepoints) {
        if (meta.uca_elements.empty()) continue;
        uca_copy.add_row({
            "{" + std::to_string(cp) + "}",
            std::to_string(meta.uca_elements[0].primary),
            std::to_string(meta.uca_elements[0].secondary),
            std::to_string(meta.uca_elements[0].tertiary)
        });
    }
    uca_copy.flush();
    txn.commit();
}

void UCDProcessor::ingest_assigned_codepoints() {
    PostgresConnection::Transaction txn(db_);
    PhysicalityStore phys_store(db_, true, true);
    AtomStore atom_store(db_, false, true);

    for (auto* meta : sorted_codepoints_) {
        auto atom_hash = BLAKE3Pipeline::hash_codepoint(meta->codepoint);
        std::vector<uint8_t> pdata(sizeof(double) * 4);
        std::memcpy(pdata.data(), meta->position.data(), sizeof(double) * 4);
        auto phys_hash = BLAKE3Pipeline::hash(pdata.data(), pdata.size());

        Eigen::Vector4d hc = (meta->position.array() + 1.0) / 2.0;
        auto hidx = hartonomous::spatial::HilbertCurve4D::encode(hc, hartonomous::spatial::HilbertCurve4D::EntityType::Atom);

        phys_store.store({phys_hash, hidx, meta->position, {meta->position}});
        atom_store.store({atom_hash, phys_hash, meta->codepoint});
    }
    phys_store.flush();
    atom_store.flush();
    txn.commit();
}

void UCDProcessor::ingest_unassigned_codepoints() {
    constexpr uint32_t MAX_CP = 0x10FFFF;
    constexpr size_t BATCH = 100000;
    std::bitset<MAX_CP + 1> assigned;
    for (auto* meta : sorted_codepoints_) assigned.set(meta->codepoint);
    
    uint32_t base_seq = static_cast<uint32_t>(sorted_codepoints_.size());
    size_t total_processed = 0;

    for (uint32_t cp_start = 0; cp_start <= MAX_CP; cp_start += BATCH) {
        PostgresConnection::Transaction txn(db_);
        PhysicalityStore phys_store(db_, true, true);
        AtomStore atom_store(db_, false, true);

        for (uint32_t cp = cp_start; cp < std::min(cp_start + (uint32_t)BATCH, MAX_CP + 1); ++cp) {
            if (assigned.test(cp)) continue;
            
            uint32_t seq = base_seq + (uint32_t)total_processed++;
            Eigen::Vector4d pos = NodeGenerator::generate_node(seq, NodeGenerator::UNICODE_TOTAL);
            std::vector<uint8_t> pdata(sizeof(double) * 4);
            std::memcpy(pdata.data(), pos.data(), sizeof(double) * 4);
            auto phys_hash = BLAKE3Pipeline::hash(pdata.data(), pdata.size());

            Eigen::Vector4d hc = (pos.array() + 1.0) / 2.0;
            auto hidx = hartonomous::spatial::HilbertCurve4D::encode(hc, hartonomous::spatial::HilbertCurve4D::EntityType::Atom);

            phys_store.store({phys_hash, hidx, pos, {pos}});
            atom_store.store({BLAKE3Pipeline::hash_codepoint(cp), phys_hash, cp});
        }
        phys_store.flush();
        atom_store.flush();
        txn.commit();
    }
}

} // namespace Hartonomous::unicode
