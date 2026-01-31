#pragma once

#include "ucd_parser.hpp"
#include "semantic_sequencer.hpp"
#include <database/postgres_connection.hpp>
#include <vector>

namespace Hartonomous::unicode {

class UCDProcessor {
public:
    UCDProcessor(const std::string& data_dir, PostgresConnection& db);

    /**
     * @brief Run the full ingestion pipeline
     */
    void process_and_ingest();

private:
    /**
     * @brief Categorize codepoints into primary groups
     */
    uint32_t get_primary_group(const std::string& gc);

    /**
     * @brief Assign sequence indices based on semantic sort
     */
    void assign_sequence();

    /**
     * @brief Map sequence to S3 and Hilbert
     */
    void generate_geometry();

    /**
     * @brief Bulk insert into DB
     */
    void ingest_to_db();

    UCDParser parser_;
    SemanticSequencer sequencer_;
    PostgresConnection& db_;
    std::vector<CodepointMetadata*> sorted_codepoints_;
};

} // namespace Hartonomous::unicode
