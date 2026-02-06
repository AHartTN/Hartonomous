#pragma once

#include "unicode/ingestor/ucd_parser.hpp"
#include "unicode/ingestor/ucd_models.hpp"
#include "unicode/ingestor/semantic_sequencer.hpp"
#include "database/postgres_connection.hpp"
#include <string>
#include <vector>

namespace Hartonomous::unicode {

/**
 * @brief Processes and ingests Unicode codepoints into the Hartonomous database.
 *
 * This class orchestrates the full Unicode ingestion pipeline:
 * 1. Parse UCD files (assigned codepoints only)
 * 2. Build semantic graph and linearize
 * 3. Compute S³ positions
 * 4. Bulk insert assigned codepoints
 * 5. Stream unassigned codepoints
 */
class UCDProcessor {
public:
    UCDProcessor(const std::string& data_dir, PostgresConnection& db);

    /**
     * @brief Run the full ingestion pipeline.
     *
     * In the split architecture, this reads from the 'ucd' schema (gene pool)
     * instead of raw files.
     */
    void process_and_ingest();

    /**
     * @brief Load all UCD metadata from the 'ucd' database schema.
     */
    void load_from_database();

private:
    void ingest_assigned_codepoints();
    void ingest_unassigned_codepoints();

    UCDParser parser_;
    SemanticSequencer sequencer_;
    PostgresConnection& db_;
    std::vector<CodepointMetadata*> sorted_codepoints_;
};

} // namespace Hartonomous::unicode
