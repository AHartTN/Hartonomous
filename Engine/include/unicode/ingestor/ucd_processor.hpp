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
     * @brief Ingest assigned codepoints (with semantic ordering)
     */
    void ingest_assigned_codepoints();

    /**
     * @brief Stream unassigned codepoints directly to DB
     * Completes the full Unicode codespace (1,114,112 codepoints)
     */
    void ingest_unassigned_codepoints();

    UCDParser parser_;
    SemanticSequencer sequencer_;
    PostgresConnection& db_;
    std::vector<CodepointMetadata*> sorted_codepoints_;
};

} // namespace Hartonomous::unicode
