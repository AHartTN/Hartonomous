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
    void ingest_unicode_data(const std::string& filepath);
    void ingest_property_file(const std::string& filepath, const std::string& default_prop = "");
    
    // Helpers
    void link_blocks_and_scripts(const std::string& directory);

private:
    DbConfig m_db_config;
    std::unique_ptr<IDatabaseConnection> m_db_connection;
};

#endif // UCD_INGESTOR_HPP
