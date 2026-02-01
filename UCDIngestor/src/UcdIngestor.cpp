// UcdIngestor.cpp
#include "UcdIngestor.hpp"
#include "PgConnection.hpp"
#include "UcdXmlReader.hpp"
#include "parsers/AllKeysParser.hpp"
#include "parsers/ConfusablesParser.hpp"
#include <iostream>
#include <algorithm>
#include <stdexcept>
#include <sstream>
#include <iomanip>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

UcdIngestor::UcdIngestor(const DbConfig& config, std::unique_ptr<IDatabaseConnection> conn)
    : m_db_config(config), m_db_connection(std::move(conn)) {}

UcdIngestor::~UcdIngestor() {
    m_db_connection->disconnect();
}

void UcdIngestor::initialize_database() {
    std::string conn_str = "host=" + m_db_config.host +
                           " user=" + m_db_config.user +
                           " password=" + m_db_config.password +
                           " dbname=" + m_db_config.dbname +
                           " port=" + m_db_config.port;
    m_db_connection->connect(conn_str);
}

// --------------------------------------------------------------------------------------
// Core "Gene Pool" Ingestion Logic
// --------------------------------------------------------------------------------------

void UcdIngestor::ingest_ucd_xml(const std::string& filepath) {
    std::cout << "Ingesting UCD XML Gene Pool from: " << filepath << std::endl;
    UcdXmlReader reader(filepath);
    reader.open();

    m_db_connection->begin_transaction();
    
    // Clear existing (optional, but good for idempotency if running full workflow)
    m_db_connection->execute_query("TRUNCATE TABLE ucd.code_points RESTART IDENTITY CASCADE;");

    int count = 0;
    const int BATCH_SIZE = 5000;
    std::stringstream sql_batch;
    bool first_in_batch = true;

    auto flush_batch = [&]() {
        if (!first_in_batch) {
            std::string sql = "INSERT INTO ucd.code_points (codepoint, hex_str, name, general_category, "
                              "canonical_combining_class, bidi_class, decomposition_type, "
                              "decomposition_mapping, numeric_value_dec, numeric_type, age, block, script, properties) VALUES " 
                              + sql_batch.str() + ";";
            m_db_connection->execute_query(sql);
            sql_batch.str("");
            sql_batch.clear();
            first_in_batch = true;
        }
    };

    while (auto atom_opt = reader.next_atom()) {
        const auto& atom = *atom_opt;
        
        if (!first_in_batch) sql_batch << ",";
        first_in_batch = false;

        // Build Properties JSON
        json props_json;
        for (const auto& [k, v] : atom.properties) {
            props_json[k] = v;
        }

        // Helper to safe-quote strings for SQL
        auto q = [](const std::string& s) { 
            // Simple escaping: replace ' with ''
            std::string out = s;
            size_t pos = 0;
            while ((pos = out.find("'", pos)) != std::string::npos) {
                out.replace(pos, 1, "''");
                pos += 2;
            }
            return "'" + out + "'";
        };
        
        auto q_nullable = [&](const std::string& s) {
            return s.empty() ? "NULL" : q(s);
        };

        // Extract Hot Columns from properties map if they exist (UcdXmlReader puts everything in properties except id/hex)
        // XML attributes: na, gc, ccc, bc, dt, dm, nv, nt, age, blk, sc
        std::string name = atom.name.empty() ? atom.properties.count("na") ? atom.properties.at("na") : "" : atom.name;
        std::string gc = atom.gc.empty() ? atom.properties.count("gc") ? atom.properties.at("gc") : "" : atom.gc;
        std::string ccc = atom.properties.count("ccc") ? atom.properties.at("ccc") : "0";
        std::string bc = atom.properties.count("bc") ? atom.properties.at("bc") : "";
        std::string dt = atom.properties.count("dt") ? atom.properties.at("dt") : "";
        std::string dm = atom.properties.count("dm") ? atom.properties.at("dm") : "";
        std::string nv = atom.properties.count("nv") ? atom.properties.at("nv") : "";
        std::string nt = atom.properties.count("nt") ? atom.properties.at("nt") : "";
        std::string age = atom.age.empty() ? atom.properties.count("age") ? atom.properties.at("age") : "" : atom.age;
        std::string blk = atom.block.empty() ? atom.properties.count("blk") ? atom.properties.at("blk") : "" : atom.block;
        std::string sc = atom.properties.count("sc") ? atom.properties.at("sc") : "";

        // Normalize '#' in names (CJK)
        if (name.find("#") != std::string::npos) {
            // Usually "CJK UNIFIED IDEOGRAPH-#" -> Replace # with Hex
            // Or handle logic. For Gene Pool, keep raw or replace?
            // UCD XML convention: # is placeholder for code point.
            // Let's NOT replace it here, keep it raw as source.
        }

        sql_batch << "(" 
                  << atom.id << ", "
                  << q(atom.hex) << ", "
                  << q_nullable(name) << ", "
                  << q_nullable(gc) << ", "
                  << (ccc.empty() ? "0" : ccc) << ", "
                  << q_nullable(bc) << ", "
                  << q_nullable(dt) << ", "
                  << q_nullable(dm) << ", "
                  << q_nullable(nv) << ", "
                  << q_nullable(nt) << ", "
                  << q_nullable(age) << ", "
                  << q_nullable(blk) << ", "
                  << q_nullable(sc) << ", "
                  << q(props_json.dump())
                  << ")";

        count++;
        if (count % BATCH_SIZE == 0) {
            flush_batch();
            std::cout << "Ingested " << count << " items..." << std::endl;
        }
    }
    
    flush_batch();
    m_db_connection->commit_transaction();
    std::cout << "Finished UCD XML Ingestion. Total: " << count << std::endl;
}

void UcdIngestor::ingest_allkeys(const std::string& filepath) {
    std::cout << "Ingesting UCA Collation Weights from: " << filepath << std::endl;
    ucd::AllKeysParser parser(filepath);
    
    m_db_connection->begin_transaction();
    m_db_connection->execute_query("TRUNCATE TABLE ucd.collation_weights RESTART IDENTITY CASCADE;");

    int count = 0;
    const int BATCH_SIZE = 5000;
    std::stringstream sql_batch;
    bool first_in_batch = true;

    auto flush_batch = [&]() {
        if (!first_in_batch) {
            std::string sql = "INSERT INTO ucd.collation_weights (source_codepoints, primary_weight, secondary_weight, tertiary_weight, is_variable) VALUES " 
                              + sql_batch.str() + ";";
            m_db_connection->execute_query(sql);
            sql_batch.str("");
            sql_batch.clear();
            first_in_batch = true;
        }
    };

    while (auto weight_opt = parser.next()) {
        const auto& w = *weight_opt;
        
        if (!first_in_batch) sql_batch << ",";
        first_in_batch = false;

        // Construct Postgres Array literal: '{1, 2, 3}'
        std::stringstream arr_ss;
        arr_ss << "'{";
        for (size_t i = 0; i < w.source_codepoints.size(); ++i) {
            if (i > 0) arr_ss << ",";
            arr_ss << w.source_codepoints[i];
        }
        arr_ss << "}'";

        sql_batch << "(" 
                  << arr_ss.str() << ", "
                  << w.primary << ", "
                  << w.secondary << ", "
                  << w.tertiary << ", "
                  << (w.is_variable ? "TRUE" : "FALSE")
                  << ")";

        count++;
        if (count % BATCH_SIZE == 0) {
            flush_batch();
            std::cout << "Ingested " << count << " collation weights..." << std::endl;
        }
    }

    flush_batch();
    m_db_connection->commit_transaction();
    std::cout << "Finished Collation Weights Ingestion. Total: " << count << std::endl;
}

void UcdIngestor::ingest_confusables(const std::string& filepath) {
    std::cout << "Ingesting Confusables from: " << filepath << std::endl;
    ucd::ConfusablesParser parser(filepath);
    
    m_db_connection->begin_transaction();
    m_db_connection->execute_query("TRUNCATE TABLE ucd.confusables RESTART IDENTITY CASCADE;");

    int count = 0;
    const int BATCH_SIZE = 5000;
    std::stringstream sql_batch;
    bool first_in_batch = true;

    auto flush_batch = [&]() {
        if (!first_in_batch) {
            std::string sql = "INSERT INTO ucd.confusables (source_codepoint, target_codepoints, confusable_type) VALUES " 
                              + sql_batch.str() + ";";
            m_db_connection->execute_query(sql);
            sql_batch.str("");
            sql_batch.clear();
            first_in_batch = true;
        }
    };

    while (auto item_opt = parser.next()) {
        const auto& item = *item_opt;
        
        if (!first_in_batch) sql_batch << ",";
        first_in_batch = false;

        // Target Array
        std::stringstream arr_ss;
        arr_ss << "'{";
        for (size_t i = 0; i < item.target_codepoints.size(); ++i) {
            if (i > 0) arr_ss << ",";
            arr_ss << item.target_codepoints[i];
        }
        arr_ss << "}'";

        sql_batch << "(" 
                  << item.source_codepoint << ", "
                  << arr_ss.str() << ", "
                  << "'" << item.type << "'" // Simple string, assumed safe
                  << ")";

        count++;
        if (count % BATCH_SIZE == 0) {
            flush_batch();
        }
    }

    flush_batch();
    m_db_connection->commit_transaction();
    std::cout << "Finished Confusables Ingestion. Total: " << count << std::endl;
}

void UcdIngestor::run_gene_pool_ingestion(
    const std::string& xml_path,
    const std::string& allkeys_path,
    const std::string& confusables_path
) {
    try {
        initialize_database();
        ingest_ucd_xml(xml_path);
        ingest_allkeys(allkeys_path);
        ingest_confusables(confusables_path);
        std::cout << "=== Gene Pool Ingestion Complete ===" << std::endl;
    } catch (const std::exception& e) {
        std::cerr << "Gene Pool Ingestion Failed: " << e.what() << std::endl;
        // Rollback handled if exception thrown during specific phase, 
        // but global catch implies we stop.
    }
}

// --------------------------------------------------------------------------------------
// Legacy Methods (Retained as Stubs or for Partial Compat if needed)
// --------------------------------------------------------------------------------------
void UcdIngestor::ingest_unicode_data(const std::string&) {}
void UcdIngestor::ingest_blocks_data(const std::string&) {}
void UcdIngestor::ingest_derived_age_data(const std::string&) {}
void UcdIngestor::ingest_property_aliases_data(const std::string&) {}
void UcdIngestor::run_ingestion_workflow(const std::string&, const std::string&, const std::string&, const std::string&) {}

// Helper caches (Unused in new pipeline but kept for class member compat)
long long UcdIngestor::get_or_insert_general_category(const std::string&, const std::string&) { return 0; }
long long UcdIngestor::get_or_insert_combining_class(int, const std::string&) { return 0; }
long long UcdIngestor::get_or_insert_bidi_class(const std::string&, const std::string&) { return 0; }
long long UcdIngestor::get_or_insert_numeric_type(const std::string&) { return 0; }
long long UcdIngestor::get_or_insert_property(const std::string&, const std::string&, const std::string&) { return 0; }
long long UcdIngestor::find_block_id_for_code_point(const std::string&) { return 0; }
long long UcdIngestor::find_age_id_for_code_point(const std::string&) { return 0; }
void UcdIngestor::populate_static_lookup_tables() {}
void UcdIngestor::load_blocks_from_db() {}
void UcdIngestor::load_ages_from_db() {}
void UcdIngestor::load_properties_from_db() {}