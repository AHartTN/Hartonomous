#ifndef UCD_INGESTOR_HPP
#define UCD_INGESTOR_HPP

#include "IDatabaseConnection.hpp"
#include "Config.hpp"
#include <string>
#include <memory>
#include <map>

class UcdIngestor {
public:
    UcdIngestor(const DbConfig& config, std::unique_ptr<IDatabaseConnection> conn);
    ~UcdIngestor();

    // The Master Command
    void run_gene_pool_ingestion(
        const std::string& xml_path,
        const std::string& allkeys_path,
        const std::string& confusables_path,
        const std::string& emoji_path,
        const std::string& emoji_zwj_path
    );

private:
    DbConfig m_db_config;
    std::unique_ptr<IDatabaseConnection> m_db_connection;

    void initialize_database();

    //--- Core Pipelines ---//
    
    // 1. The Encyclopedia (ucd.code_points)
    void ingest_ucd_xml(const std::string& filepath);

    // 2. The Logic (ucd.collation_weights)
    void ingest_allkeys(const std::string& filepath);

    // 3. The Security Graph (ucd.confusables)
    void ingest_confusables(const std::string& filepath);

    // 4. The Composites (ucd.emoji_sequences)
    // Handles both emoji-sequences.txt and emoji-zwj-sequences.txt
    void ingest_emoji_sequences(const std::string& filepath, const std::string& type_tag);
};

#endif // UCD_INGESTOR_HPP
