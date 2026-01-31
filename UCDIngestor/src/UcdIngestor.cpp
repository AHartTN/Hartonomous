#include "UcdIngestor.hpp"
#include "PgConnection.hpp"
#include "parsers/PropertyParser.hpp"
#include <iostream>
#include <filesystem>
#include <algorithm>
#include <fstream>
#include <sstream>
#include <vector>
#include <map>
#include <set>

namespace fs = std::filesystem;

// Helper: Trim
static std::string trim(const std::string& str) {
    size_t first = str.find_first_not_of(" \t");
    if (std::string::npos == first) return str;
    size_t last = str.find_last_not_of(" \t");
    return str.substr(first, (last - first + 1));
}

// Helper: Hex Parse
static long long parse_hex(const std::string& s) {
    try {
        std::string h = trim(s);
        if (h.empty()) return -1;
        return std::stoll(h, nullptr, 16);
    } catch(...) { return -1; }
}

UcdIngestor::UcdIngestor(const DbConfig& config, std::unique_ptr<IDatabaseConnection> conn)
    : m_db_config(config), m_db_connection(std::move(conn)) {}

UcdIngestor::~UcdIngestor() { if (m_db_connection) m_db_connection->disconnect(); }

void UcdIngestor::connect() {
    std::string conn_str = "dbname=" + m_db_config.dbname;
    if (!m_db_config.host.empty()) conn_str += " host=" + m_db_config.host;
    if (!m_db_config.port.empty()) conn_str += " port=" + m_db_config.port;
    if (!m_db_config.user.empty()) conn_str += " user=" + m_db_config.user;
    if (!m_db_config.password.empty()) conn_str += " password=" + m_db_config.password;
    m_db_connection->connect(conn_str);
}

void UcdIngestor::execute_sql(const std::string& sql) { m_db_connection->execute_query(sql); }

// ------------------------------------------------------------------------------------------------
// LOOKUP INGESTION
// ------------------------------------------------------------------------------------------------

void UcdIngestor::ingest_property_value_aliases(const std::string& filepath) {
    if (!fs::exists(filepath)) return;
    std::cout << "[Lookup] Ingesting PropertyValueAliases.txt..." << std::endl;
    std::ifstream file(filepath);

    std::vector<std::map<std::string, std::string>> gc_batch, bc_batch, sc_batch;
    m_db_connection->begin_transaction();

    std::string line;
    while (std::getline(file, line)) {
        size_t hash = line.find('#');
        if (hash != std::string::npos) line = line.substr(0, hash);
        if (trim(line).empty()) continue;

        std::vector<std::string> parts;
        std::stringstream ss(line);
        std::string part;
        while(std::getline(ss, part, ';')) parts.push_back(trim(part));
        
        if (parts.size() < 2) continue;

        std::string type = parts[0];
        std::string code = parts[1];
        std::string name = (parts.size() > 2) ? parts[2] : code;

        if (type == "gc") {
             gc_batch.push_back({{"code", code}, {"description", name}});
        } else if (type == "bc") {
             bc_batch.push_back({{"code", code}, {"description", name}});
        } else if (type == "sc") {
             sc_batch.push_back({{"iso_code", code}, {"name", name}});
        }
    }
    
    if (!gc_batch.empty()) m_db_connection->bulk_stream_upsert("general_categories", gc_batch, "code", {"description"});
    if (!bc_batch.empty()) m_db_connection->bulk_stream_upsert("bidi_classes", bc_batch, "code", {"description"});
    if (!sc_batch.empty()) m_db_connection->bulk_stream_upsert("scripts", sc_batch, "name", {"iso_code"}); // Update ISO if present
    
    m_db_connection->commit_transaction();
}

void UcdIngestor::ingest_blocks(const std::string& filepath) {
    if (!fs::exists(filepath)) return;
    std::cout << "[Lookup] Ingesting Blocks.txt..." << std::endl;
    PropertyParser parser;
    std::vector<std::map<std::string, std::string>> batch;
    m_db_connection->begin_transaction();

    parser.parse(filepath, [&](std::map<std::string, std::string> row) {
        long long s = parse_hex(row["start_cp"]);
        long long e = parse_hex(row["end_cp"]);
        std::string name = row["raw_p1"];
        if (s >= 0 && !name.empty()) {
            batch.push_back({{"name", name}, {"start_cp", std::to_string(s)}, {"end_cp", std::to_string(e)}});
        }
    });

    if (!batch.empty()) m_db_connection->bulk_stream_upsert("blocks", batch, "name", {"start_cp", "end_cp"});
    m_db_connection->commit_transaction();
}

void UcdIngestor::ingest_scripts(const std::string& filepath) {
    if (!fs::exists(filepath)) return;
    std::cout << "[Lookup] Verifying Scripts from Scripts.txt..." << std::endl;
    PropertyParser parser;
    std::vector<std::map<std::string, std::string>> batch;
    std::set<std::string> seen;
    
    m_db_connection->begin_transaction();
    parser.parse(filepath, [&](std::map<std::string, std::string> row) {
        std::string name = row["raw_p1"];
        if (!name.empty() && seen.find(name) == seen.end()) {
            batch.push_back({{"name", name}});
            seen.insert(name);
        }
    });
    if (!batch.empty()) m_db_connection->bulk_stream_upsert("scripts", batch, "name", {}); 
    m_db_connection->commit_transaction();
}

// ------------------------------------------------------------------------------------------------
// CORE INGESTION
// ------------------------------------------------------------------------------------------------

void UcdIngestor::ingest_unicode_data(const std::string& filepath) {
    if (!fs::exists(filepath)) return;
    std::cout << "[Core] Ingesting UnicodeData.txt..." << std::endl;
    
    m_db_connection->begin_transaction();
    m_db_connection->execute_query("DROP TABLE IF EXISTS staging_unicode_data");
    m_db_connection->execute_query(R"(
        CREATE TEMP TABLE staging_unicode_data (
            codepoint INTEGER,
            name TEXT,
            gc_code TEXT,
            ccc INTEGER,
            bc_code TEXT,
            decomp_full TEXT,
            num_dec TEXT,
            num_dig TEXT,
            num_val TEXT,
            bidi_mirrored TEXT,
            old_name TEXT,
            iso_comment TEXT,
            upper_map INTEGER,
            lower_map INTEGER,
            title_map INTEGER
        ) ON COMMIT DROP
    )");
    
    std::ifstream file(filepath);
    std::string line;
    std::vector<std::map<std::string, std::string>> batch;
    
    while(std::getline(file, line)) {
        if(line.empty()) continue;
        std::vector<std::string> parts;
        std::stringstream ss(line);
        std::string part;
        while(std::getline(ss, part, ';')) parts.push_back(part);
        if (parts.size() < 15) continue;
        
        std::map<std::string, std::string> row;
        row["codepoint"] = std::to_string(parse_hex(parts[0]));
        row["name"] = parts[1];
        row["gc_code"] = parts[2];
        row["ccc"] = parts[3].empty() ? "0" : parts[3];
        row["bc_code"] = parts[4];
        row["decomp_full"] = parts[5];
        row["num_dec"] = parts[6];
        row["num_dig"] = parts[7];
        row["num_val"] = parts[8];
        row["bidi_mirrored"] = parts[9];
        row["old_name"] = parts[10];
        row["iso_comment"] = parts[11];
        row["upper_map"] = parts[12].empty() ? "" : std::to_string(parse_hex(parts[12]));
        row["lower_map"] = parts[13].empty() ? "" : std::to_string(parse_hex(parts[13]));
        row["title_map"] = parts[14].empty() ? "" : std::to_string(parse_hex(parts[14]));
        
        batch.push_back(row);
        
        if (batch.size() >= 10000) {
            m_db_connection->bulk_stream_upsert("staging_unicode_data", batch, "", {});
            batch.clear();
        }
    }
    if (!batch.empty()) m_db_connection->bulk_stream_upsert("staging_unicode_data", batch, "", {});
    
    std::cout << "[Core] Transforming Staging to Normalized..." << std::endl;
    
    // Numeric value safe cast logic
    // Using simple Integers for Codepoint/Maps now
    std::string sql = R"(
        INSERT INTO code_points (
            codepoint, name, general_category_id, combining_class, bidi_class_id,
            decomposition_type, decomposition_mapping,
            numeric_value_decimal, numeric_value_digit, numeric_value_numeric,
            bidi_mirrored, unicode_1_name, iso_comment,
            simple_uppercase_mapping, simple_lowercase_mapping, simple_titlecase_mapping
        )
        SELECT
            s.codepoint,
            s.name,
            gc.id,
            s.ccc,
            bc.id,
            CASE WHEN s.decomp_full LIKE '<%>%' THEN substring(s.decomp_full from '^<[^>]+>') ELSE NULL END,
            CASE WHEN s.decomp_full LIKE '<%>%' THEN substring(s.decomp_full from '>(.*)') ELSE NULLIF(s.decomp_full, '') END,
            NULLIF(s.num_dec, '')::int,
            NULLIF(s.num_dig, '')::int,
            CASE 
                WHEN s.num_val LIKE '%/%' THEN 
                    CAST(split_part(s.num_val, '/', 1) AS DOUBLE PRECISION) / CAST(split_part(s.num_val, '/', 2) AS DOUBLE PRECISION)
                ELSE 
                    CAST(NULLIF(s.num_val, '') AS DOUBLE PRECISION)
            END,
            (s.bidi_mirrored = 'Y'),
            s.old_name,
            s.iso_comment,
            s.upper_map,
            s.lower_map,
            s.title_map
        FROM staging_unicode_data s
        LEFT JOIN general_categories gc ON s.gc_code = gc.code
        LEFT JOIN bidi_classes bc ON s.bc_code = bc.code
        ON CONFLICT (codepoint) DO UPDATE SET
            name = EXCLUDED.name;
    )";

    m_db_connection->execute_query(sql);
    m_db_connection->commit_transaction();
}

// ------------------------------------------------------------------------------------------------
// POST-PROCESSING
// ------------------------------------------------------------------------------------------------

void UcdIngestor::link_blocks_and_scripts(const std::string& directory) {
    std::cout << "[Linking] Updating Block and Script Relations..." << std::endl;
    
    m_db_connection->begin_transaction();
    
    // 1. Link Blocks
    m_db_connection->execute_query(R"(
        UPDATE code_points c
        SET block_id = b.id
        FROM blocks b
        WHERE c.codepoint BETWEEN b.start_cp AND b.end_cp
    )");
    
    // 2. Link Scripts (Need Ranges from Scripts.txt)
    std::string scripts_path = directory + "Scripts.txt";
    if (fs::exists(scripts_path)) {
        PropertyParser parser;
        // Temp table
        m_db_connection->execute_query("CREATE TEMP TABLE temp_script_ranges (start_cp INT, end_cp INT, script_name TEXT) ON COMMIT DROP");
        std::vector<std::map<std::string, std::string>> batch;
        
        parser.parse(scripts_path, [&](std::map<std::string, std::string> row) {
            batch.push_back({
                {"start_cp", std::to_string(parse_hex(row["start_cp"]))},
                {"end_cp", std::to_string(parse_hex(row["end_cp"]))},
                {"script_name", row["raw_p1"]}
            });
            if (batch.size()>=5000) {
                m_db_connection->bulk_stream_upsert("temp_script_ranges", batch, "", {});
                batch.clear();
            }
        });
        if (!batch.empty()) m_db_connection->bulk_stream_upsert("temp_script_ranges", batch, "", {});
        
        m_db_connection->execute_query(R"(
            UPDATE code_points c
            SET script_id = s.id
            FROM temp_script_ranges r
            JOIN scripts s ON r.script_name = s.name
            WHERE c.codepoint BETWEEN r.start_cp AND r.end_cp
        )");
    }
    m_db_connection->commit_transaction();
}

void UcdIngestor::ingest_directory(const std::string& directory) {
    // 1. Lookups
    ingest_property_value_aliases(directory + "PropertyValueAliases.txt");
    ingest_blocks(directory + "Blocks.txt");
    ingest_scripts(directory + "Scripts.txt");
    
    // 2. Core Atoms
    ingest_unicode_data(directory + "UnicodeData.txt");
    
    // 3. Linkage
    link_blocks_and_scripts(directory);
    
    // 4. Extended Properties (Iterate Directory)
    // Filter for known property files or "Derived" files
    for (const auto& entry : fs::directory_iterator(directory)) {
        std::string name = entry.path().filename().string();
        
        // Skip files we already handled
        if (name == "UnicodeData.txt" || name == "Blocks.txt" || name == "Scripts.txt" || name == "PropertyValueAliases.txt") continue;
        if (name.find("ReadMe") != std::string::npos || name == "Index.txt") continue;
        
        // Known formats
        if (name == "PropList.txt" || name.find("Derived") == 0 || name == "EmojiSources.txt") {
            ingest_property_file(entry.path().string());
        }
    }
}

void UcdIngestor::ingest_property_file(const std::string& filepath, const std::string& default_prop) {
    std::cout << "[Prop] Ingesting " << fs::path(filepath).filename().string() << std::endl;
    PropertyParser parser;
    std::vector<std::map<std::string, std::string>> batch;
    m_db_connection->begin_transaction();
    
    // Pre-fetch Properties to avoid frequent inserts
    std::map<std::string, int> prop_ids;
    // (Optimization: Load all existing props)
    // For now, we do on-demand caching per file
    
    parser.parse(filepath, [&](std::map<std::string, std::string> row) {
        std::string name = default_prop.empty() ? row["raw_p1"] : default_prop;
        if (name.empty()) return;
        
        if (prop_ids.find(name) == prop_ids.end()) {
             // Safe upsert
             std::string safe = name; // basic escaping?
             m_db_connection->execute_query("INSERT INTO properties (name) VALUES ('" + safe + "') ON CONFLICT DO NOTHING");
             // Fetch ID (using RETURNING in insert or select)
             // Using sub-optimal select here but it's once per Property Name (very few)
             auto res = m_db_connection->execute_query("SELECT id FROM properties WHERE name='" + safe + "'");
             if (res->size() > 0) prop_ids[name] = std::stoi(res->at(0,0));
        }
        int pid = prop_ids[name];
        
        long long s = parse_hex(row["start_cp"]);
        long long e = parse_hex(row["end_cp"]);
        
        for (long long cp = s; cp <= e; ++cp) {
            batch.push_back({
                {"codepoint", std::to_string(cp)},
                {"property_id", std::to_string(pid)}
            });
        }
        if (batch.size() >= 5000) {
            m_db_connection->bulk_stream_upsert("code_point_properties", batch, "code_point_properties_pkey", {});
            batch.clear();
        }
    });
    if (!batch.empty()) m_db_connection->bulk_stream_upsert("code_point_properties", batch, "code_point_properties_pkey", {});
    m_db_connection->commit_transaction();
}
