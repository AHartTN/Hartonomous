// main.cpp
#include "Config.hpp"
#include "PgConnection.hpp"
#include "UcdIngestor.hpp"

#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

int main(int argc, char* argv[]) {
    if (argc != 4) {
        std::cerr << "Usage: " << argv[0] << " <ucd.all.flat.xml> <allkeys.txt> <confusables.txt>" << std::endl;
        return 1;
    }

    std::string xml_path = argv[1];
    std::string allkeys_path = argv[2];
    std::string confusables_path = argv[3];

    try {
        DbConfig db_config = DbConfig::loadFromEnv();
        std::unique_ptr<IDatabaseConnection> db_conn = std::make_unique<PgConnection>();

        UcdIngestor ingestor(db_config, std::move(db_conn));
        ingestor.run_gene_pool_ingestion(xml_path, allkeys_path, confusables_path);

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }

    return 0;
}