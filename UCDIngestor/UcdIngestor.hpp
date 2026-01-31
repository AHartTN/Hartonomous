// UcdIngestor.hpp
#ifndef UCD_INGESTOR_HPP
#define UCD_INGESTOR_HPP

#include "Config.hpp"
#include "IDatabaseConnection.hpp"
#include "UcdFileReader.hpp" // Includes parsers and models
#include "UcdModels.hpp"

#include <string>
#include <memory>
#include <map>
#include <stdexcept>
#include <vector>
#include <optional> // For optional values in models

// Forward declarations for comparing unique_ptrs
// These are needed for std::sort and std::upper_bound with unique_ptrs
namespace { // Anonymous namespace for internal linkage
    bool compare_blocks(const std::unique_ptr<Block>& a, const std::unique_ptr<Block>& b) {
        return a->start_code_int < b->start_code_int;
    }
    bool compare_ages(const std::unique_ptr<Age>& a, const std::unique_ptr<Age>& b) {
        return a->start_code_int < b->start_code_int;
    }
}


class UcdIngestor {
private:
    DbConfig m_db_config;
    std::unique_ptr<IDatabaseConnection> m_db_connection;

    // Caches for lookup tables to minimize DB queries
    std::map<std::string, long long> m_general_category_cache; // short_code -> id
    std::map<int, long long> m_combining_class_cache;        // value -> id
    std::map<std::string, long long> m_bidi_class_cache;       // short_code -> id
    std::map<std::string, long long> m_numeric_type_cache;     // type_name -> id
    std::map<std::string, long long> m_property_cache;         // short_name -> id

    // Caches for range-based lookups
    std::vector<std::unique_ptr<Block>> m_blocks_cache;
    std::vector<std::unique_ptr<Age>> m_ages_cache;

    // Helper functions for initial population of lookup tables
    void populate_static_lookup_tables();
    void load_blocks_from_db();
    void load_ages_from_db();
    void load_properties_from_db(); // New: to load properties for binary/string props

    // Helper to get or insert lookup ID
    long long get_or_insert_general_category(const std::string& short_code, const std::string& description);
    long long get_or_insert_combining_class(int value, const std::string& description);
    long long get_or_insert_bidi_class(const std::string& short_code, const std::string& description);
    long long get_or_insert_numeric_type(const std::string& type_name);
    long long get_or_insert_property(const std::string& short_name, const std::string& long_name, const std::string& category);

    // Helper to find ID for a code point based on ranges
    long long find_block_id_for_code_point(const std::string& code_point_hex);
    long long find_age_id_for_code_point(const std::string& code_point_hex);


public:
    UcdIngestor(const DbConfig& config, std::unique_ptr<IDatabaseConnection> conn);
    ~UcdIngestor();

    void initialize_database(); // Connects and prepares caches
    void ingest_unicode_data(const std::string& filepath);
    void ingest_blocks_data(const std::string& filepath);
    void ingest_derived_age_data(const std::string& filepath);
    void ingest_property_aliases_data(const std::string& filepath);

    // Main ingestion method
    void ingest_all_ucd_files(
        const std::string& unicode_data_path,
        const std::string& blocks_path,
        const std::string& derived_age_path,
        const std::string& property_aliases_path
    );

    // Function to run the full ingestion workflow
    void run_ingestion_workflow(
        const std::string& unicode_data_path,
        const std::string& blocks_path,
        const std::string& derived_age_path,
        const std::string& property_aliases_path
    );
};

#endif // UCD_INGESTOR_HPP
