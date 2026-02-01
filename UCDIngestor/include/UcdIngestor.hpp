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

    void connect();
    void execute_sql(const std::string& sql);
    
    // Master Pipeline
    void ingest_directory(const std::string& directory);
    
    // Core Steps (Public for granular testing if needed)
    void ingest_property_value_aliases(const std::string& filepath);
    void ingest_blocks(const std::string& filepath);
    void ingest_scripts(const std::string& filepath);
    void ingest_property_file(const std::string& filepath, const std::string& default_prop = "");

    // Helpers
    void link_blocks_and_scripts(const std::string& directory);

    // Main ingestion method (XML-based gene pool)
    void run_gene_pool_ingestion(
        const std::string& xml_path,
        const std::string& allkeys_path,
        const std::string& confusables_path
    );

private:
    DbConfig m_db_config;
    std::unique_ptr<IDatabaseConnection> m_db_connection;
    void initialize_database(); // Connects and prepares caches
    
    // Core Ingestion Methods (New Architecture)
    void ingest_ucd_xml(const std::string& filepath);
    void ingest_allkeys(const std::string& filepath);
    void ingest_confusables(const std::string& filepath);

    // Legacy methods (Stubbed for compatibility)
    void ingest_unicode_data(const std::string& filepath);
    void ingest_blocks_data(const std::string& filepath);
    void ingest_derived_age_data(const std::string& filepath);
    void ingest_property_aliases_data(const std::string& filepath);
    void run_ingestion_workflow(
        const std::string& unicode_data_path,
        const std::string& blocks_path,
        const std::string& derived_age_path,
        const std::string& property_aliases_path
    );

    // Legacy Helper Caches (Stubbed)
    long long get_or_insert_general_category(const std::string& short_code, const std::string& description);
    long long get_or_insert_combining_class(int value, const std::string& description);
    long long get_or_insert_bidi_class(const std::string& short_code, const std::string& description);
    long long get_or_insert_numeric_type(const std::string& type_name);
    long long get_or_insert_property(const std::string& short_name, const std::string& long_name, const std::string& category);
    long long find_block_id_for_code_point(const std::string& code_point_hex);
    long long find_age_id_for_code_point(const std::string& code_point_hex);
    void populate_static_lookup_tables();
    void load_blocks_from_db();
    void load_ages_from_db();
    void load_properties_from_db();
};

#endif // UCD_INGESTOR_HPP
