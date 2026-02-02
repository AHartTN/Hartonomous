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
#include <regex>
#include <fstream>
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

void UcdIngestor::ingest_ucd_xml(const std::string& filepath) {
    std::cout << "Ingesting UCD XML Gene Pool from: " << filepath << std::endl;
    UcdXmlReader reader(filepath);
    reader.open();

    m_db_connection->begin_transaction();
    m_db_connection->execute_query("TRUNCATE TABLE ucd.code_points RESTART IDENTITY CASCADE;");

    int count = 0;
    const int BATCH_SIZE = 50000;
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

        json props_json;

        auto expand_hash = [&](std::string& val, const std::string& hex) {
            if (val == "#") { val = hex; } 
            else {
                size_t pos = 0;
                while ((pos = val.find("#", pos)) != std::string::npos) {
                    val.replace(pos, 1, hex);
                    pos += hex.length();
                }
            }
        };

        std::string hex_upper = atom.hex;
        size_t first_nonzero = hex_upper.find_first_not_of('0');
        std::string hex_short = (first_nonzero == std::string::npos) ? "0" : hex_upper.substr(first_nonzero);

        for (auto const& [k, v] : atom.properties) {
            std::string val_copy = v;
            if (val_copy.find("#") != std::string::npos) {
                if (k == "na" || k == "na1") expand_hash(val_copy, hex_short);
                else expand_hash(val_copy, hex_upper);
            }
            props_json[k] = val_copy;
        }

        auto q = [](const std::string& s) { 
            std::string out = s;
            size_t pos = 0;
            while ((pos = out.find("'", pos)) != std::string::npos) {
                out.replace(pos, 1, "''");
                pos += 2;
            }
            return "'" + out + "'";
        };
        
        auto q_nullable = [&](const std::string& s) { return s.empty() ? "NULL" : q(s); };

        std::string name = atom.name.empty() ? (atom.properties.count("na") ? atom.properties.at("na") : "") : atom.name;
        if (name.find("#") != std::string::npos) expand_hash(name, hex_short);

        std::string gc = atom.gc.empty() ? (atom.properties.count("gc") ? atom.properties.at("gc") : "") : atom.gc;
        std::string ccc = atom.properties.count("ccc") ? atom.properties.at("ccc") : "0";
        std::string bc = atom.properties.count("bc") ? atom.properties.at("bc") : "";
        std::string dt = atom.properties.count("dt") ? atom.properties.at("dt") : "";
        std::string dm = atom.properties.count("dm") ? atom.properties.at("dm") : "";
        std::string nv = atom.properties.count("nv") ? atom.properties.at("nv") : "";
        std::string nt = atom.properties.count("nt") ? atom.properties.at("nt") : "";
        std::string age = atom.age.empty() ? (atom.properties.count("age") ? atom.properties.at("age") : "") : atom.age;
        std::string blk = atom.block.empty() ? (atom.properties.count("blk") ? atom.properties.at("blk") : "") : atom.block;
        std::string sc = atom.properties.count("sc") ? atom.properties.at("sc") : "";

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

        if (++count % BATCH_SIZE == 0) {
            flush_batch();
            std::cout << "Ingested " << count << " items...\r" << std::flush;
        }
    }
    flush_batch();
    m_db_connection->commit_transaction();
    std::cout << "\nFinished UCD XML Ingestion. Total: " << count << std::endl;
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

        std::stringstream arr_ss;
        arr_ss << "'{";
        for (size_t i = 0; i < w.source_codepoints.size(); ++i) {
            if (i > 0) arr_ss << ",";
            arr_ss << w.source_codepoints[i];
        }
        arr_ss << "}'";

        sql_batch << "(" << arr_ss.str() << ", " << w.primary << ", " << w.secondary << ", " << w.tertiary << ", " << (w.is_variable ? "TRUE" : "FALSE") << ")";

        if (++count % BATCH_SIZE == 0) {
            flush_batch();
            std::cout << "Ingested " << count << " collation weights...\r" << std::flush;
        }
    }
    flush_batch();
    m_db_connection->commit_transaction();
    std::cout << "\nFinished Collation Weights Ingestion. Total: " << count << std::endl;
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

        std::stringstream arr_ss;
        arr_ss << "'{";
        for (size_t i = 0; i < item.target_codepoints.size(); ++i) {
            if (i > 0) arr_ss << ",";
            arr_ss << item.target_codepoints[i];
        }
        arr_ss << "}'";

        sql_batch << "(" << item.source_codepoint << ", " << arr_ss.str() << ", '" << item.type << "')";

        if (++count % BATCH_SIZE == 0) {
            flush_batch();
            std::cout << "Ingested " << count << " confusables...\r" << std::flush;
        }
    }
    flush_batch();
    m_db_connection->commit_transaction();
    std::cout << "\nFinished Confusables Ingestion. Total: " << count << std::endl;
}

void UcdIngestor::ingest_emoji_sequences(const std::string& filepath, const std::string& type_tag) {
    std::cout << "Ingesting Emoji Sequences (" << type_tag << ") from: " << filepath << std::endl;
    std::ifstream file(filepath);
    if (!file.is_open()) throw std::runtime_error("Cannot open emoji file: " + filepath);

    m_db_connection->begin_transaction();
    if (type_tag == "Standard") 
        m_db_connection->execute_query("TRUNCATE TABLE ucd.emoji_sequences RESTART IDENTITY CASCADE;");

    std::string line;
    int count = 0;
    std::stringstream sql_batch;
    bool first = true;
    const int BATCH_SIZE = 1000;

    auto flush = [&]() {
        if (!first) {
            std::string sql = "INSERT INTO ucd.emoji_sequences (sequence_codepoints, type_field, description) VALUES " + sql_batch.str() + " ON CONFLICT DO NOTHING;";
            m_db_connection->execute_query(sql);
            sql_batch.str("");
            first = true;
        }
    };

    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;
        size_t semi = line.find(';');
        if (semi == std::string::npos) continue;
        
        std::string hex_seq = line.substr(0, semi);
        hex_seq.erase(hex_seq.find_last_not_of(" \t") + 1);
        hex_seq.erase(0, hex_seq.find_first_not_of(" \t"));

        std::string remaining = line.substr(semi + 1);
        size_t next_semi = remaining.find(';');
        size_t hash_pos = remaining.find('#');
        
        std::string type, desc;
        if (next_semi != std::string::npos && (hash_pos == std::string::npos || next_semi < hash_pos)) {
            type = remaining.substr(0, next_semi);
            size_t desc_start = next_semi + 1;
            if (hash_pos != std::string::npos) desc = remaining.substr(desc_start, hash_pos - desc_start);
            else desc = remaining.substr(desc_start);
        } else {
            if (hash_pos != std::string::npos) {
                type = remaining.substr(0, hash_pos);
                desc = remaining.substr(hash_pos + 1);
            } else {
                type = remaining;
                desc = "";
            }
        }

        type.erase(type.find_last_not_of(" \t") + 1);
        type.erase(0, type.find_first_not_of(" \t"));
        desc.erase(desc.find_last_not_of(" \t") + 1);
        desc.erase(0, desc.find_first_not_of(" \t"));

        std::stringstream array_fmt;
        array_fmt << "'{";
        std::stringstream ss(hex_seq);
        std::string segment;
        bool first_seg = true;
        
        if (hex_seq.find(".." ) != std::string::npos) {
            size_t dots = hex_seq.find("..");
            long start = std::stol(hex_seq.substr(0, dots), nullptr, 16);
            long end = std::stol(hex_seq.substr(dots+2), nullptr, 16);
            for (long i = start; i <= end; ++i) {
                if (!first_seg) array_fmt << ",";
                array_fmt << i;
                first_seg = false;
            }
        } else {
            while (std::getline(ss, segment, ' ')) {
                if (segment.empty()) continue;
                if (!first_seg) array_fmt << ",";
                array_fmt << std::stol(segment, nullptr, 16);
                first_seg = false;
            }
        }
        array_fmt << "}'";

        if (!first) sql_batch << ",";
        first = false;

        std::string safe_desc = desc;
        size_t p = 0; while ((p = safe_desc.find("'", p)) != std::string::npos) { safe_desc.replace(p, 1, "''"); p += 2; }

        sql_batch << "(" << array_fmt.str() << ", '" << type << "', '" << safe_desc << "')";
        if (++count % BATCH_SIZE == 0) {
            flush();
            std::cout << "Ingested " << count << " emoji entries...\r" << std::flush;
        }
    }
    flush();
    m_db_connection->commit_transaction();
    std::cout << "\nFinished Emoji Sequences Ingestion. Total: " << count << std::endl;
}

void UcdIngestor::run_gene_pool_ingestion(const std::string& xml_path, const std::string& allkeys_path, const std::string& confusables_path, const std::string& emoji_path, const std::string& emoji_zwj_path) {
    try {
        initialize_database();
        ingest_ucd_xml(xml_path);
        ingest_allkeys(allkeys_path);
        ingest_confusables(confusables_path);
        ingest_emoji_sequences(emoji_path, "Standard");
        ingest_emoji_sequences(emoji_zwj_path, "ZWJ");
        std::cout << "=== Gene Pool Ingestion Complete ===" << std::endl;
    } catch (const std::exception& e) {
        std::cerr << "Gene Pool Ingestion Failed: " << e.what() << std::endl;
        throw;
    }
}
