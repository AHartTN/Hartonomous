// main.cpp
#include "Config.hpp"
#include "PgConnection.hpp"
#include "UcdIngestor.hpp"

#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <map>

int main(int argc, char* argv[]) {
    if (argc != 5) {
        std::cerr << "Usage: " << argv[0] << " <UnicodeData.txt> <Blocks.txt> <DerivedAge.txt> <PropertyAliases.txt>" << std::endl;
        return 1;
    }

    std::string unicode_data_path = argv[1];
    std::string blocks_path = argv[2];
    std::string derived_age_path = argv[3];
    std::string property_aliases_path = argv[4];

    try {
        // 1. Load database configuration from environment variables
        DbConfig db_config = DbConfig::loadFromEnv();
        
        // 2. Create a concrete database connection object
        std::unique_ptr<IDatabaseConnection> db_conn = std::make_unique<PgConnection>();

        // 3. Create the UcdIngestor and run the ingestion workflow
        UcdIngestor ingestor(db_config, std::move(db_conn));
        ingestor.run_ingestion_workflow(unicode_data_path, blocks_path, derived_age_path, property_aliases_path);

        std::cout << "UCD data ingestion process finished." << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }

    return 0;
}
