// UcdIngestor.cpp
#include "UcdIngestor.hpp"
#include "PgConnection.hpp" // Concrete DB connection type
#include <unicode/codepoint_projection.hpp> // Engine Physics
#include <iostream>
#include <algorithm> // For std::sort, std::upper_bound
#include <stdexcept>
#include <sstream>
#include <iomanip>

// Helper for sorting ranges for efficient lookup (defined in UcdIngestor.hpp anonymous namespace)

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

    // Populate static/known lookup tables and caches
    populate_static_lookup_tables();
    load_blocks_from_db();
    load_ages_from_db();
    load_properties_from_db(); // Load properties to cache
}

void UcdIngestor::populate_static_lookup_tables() {
    m_db_connection->begin_transaction();

    // --- General Categories (descriptions are derived from UCD documentation) ---
    get_or_insert_general_category("Lu", "Letter, Uppercase");
    get_or_insert_general_category("Ll", "Letter, Lowercase");
    get_or_insert_general_category("Lt", "Letter, Titlecase");
    get_or_insert_general_category("Lm", "Letter, Modifier");
    get_or_insert_general_category("Lo", "Letter, Other");
    get_or_insert_general_category("Mn", "Mark, Nonspacing");
    get_or_insert_general_category("Mc", "Mark, Spacing Combining");
    get_or_insert_general_category("Me", "Mark, Enclosing");
    get_or_insert_general_category("Nd", "Number, Decimal Digit");
    get_or_insert_general_category("Nl", "Number, Letter");
    get_or_insert_general_category("No", "Number, Other");
    get_or_insert_general_category("Pc", "Punctuation, Connector");
    get_or_insert_general_category("Pd", "Punctuation, Dash");
    get_or_insert_general_category("Ps", "Punctuation, Open");
    get_or_insert_general_category("Pe", "Punctuation, Close");
    get_or_insert_general_category("Pi", "Punctuation, Initial Quote");
    get_or_insert_general_category("Pf", "Punctuation, Final Quote");
    get_or_insert_general_category("Po", "Punctuation, Other");
    get_or_insert_general_category("Sm", "Symbol, Math");
    get_or_insert_general_category("Sc", "Symbol, Currency");
    get_or_insert_general_category("Sk", "Symbol, Modifier");
    get_or_insert_general_category("So", "Symbol, Other");
    get_or_insert_general_category("Zs", "Separator, Space");
    get_or_insert_general_category("Zl", "Separator, Line");
    get_or_insert_general_category("Zp", "Separator, Paragraph");
    get_or_insert_general_category("Cc", "Other, Control");
    get_or_insert_general_category("Cf", "Other, Format");
    get_or_insert_general_category("Cs", "Other, Surrogate");
    get_or_insert_general_category("Co", "Other, Private Use");
    get_or_insert_general_category("Cn", "Other, Not Assigned"); // Represents missing/unassigned

    // --- Combining Classes (descriptions from UCD documentation, e.g., DerivedCombiningClass.txt) ---
    get_or_insert_combining_class(0, "Not Reordered");
    get_or_insert_combining_class(1, "Overlay");
    get_or_insert_combining_class(7, "Nukta");
    get_or_insert_combining_class(8, "Kana Voicing");
    get_or_insert_combining_class(9, "Virama");
    // These are examples, the full set should be derived from UCD files or docs.
    get_or_insert_combining_class(220, "Attached Below");
    get_or_insert_combining_class(230, "Attached Above");

    // --- Bidi Classes (descriptions from UCD documentation) ---
    get_or_insert_bidi_class("L", "Left-to-Right");
    get_or_insert_bidi_class("R", "Right-to-Left");
    get_or_insert_bidi_class("AL", "Arabic Letter");
    get_or_insert_bidi_class("EN", "European Number");
    get_or_insert_bidi_class("ES", "European Separator");
    get_or_insert_bidi_class("ET", "European Terminator");
    get_or_insert_bidi_class("AN", "Arabic Number");
    get_or_insert_bidi_class("CS", "Common Separator");
    get_or_insert_bidi_class("NSM", "Nonspacing Mark");
    get_or_insert_bidi_class("BN", "Boundary Neutral");
    get_or_insert_bidi_class("B", "Paragraph Separator");
    get_or_insert_bidi_class("S", "Segment Separator");
    get_or_insert_bidi_class("WS", "Whitespace");
    get_or_insert_bidi_class("ON", "Other Neutrals");
    get_or_insert_bidi_class("LRE", "Left-to-Right Embedding");
    get_or_insert_bidi_class("LRO", "Left-to-Right Override");
    get_or_insert_bidi_class("RLE", "Right-to-Left Embedding");
    get_or_insert_bidi_class("RLO", "Right-to-Left Override");
    get_or_insert_bidi_class("PDF", "Pop Directional Format");
    get_or_insert_bidi_class("LRI", "Left-to-Right Isolate");
    get_or_insert_bidi_class("RLI", "Right-to-Left Isolate");
    get_or_insert_bidi_class("FSI", "First Strong Isolate");
    get_or_insert_bidi_class("PDI", "Pop Directional Isolate");

    // --- Numeric Types (empty string for non-numeric, as in UnicodeData.txt) ---
    get_or_insert_numeric_type(""); // Corresponds to empty field in UnicodeData.txt
    get_or_insert_numeric_type("Decimal");
    get_or_insert_numeric_type("Digit");
    get_or_insert_numeric_type("Numeric");

    m_db_connection->commit_transaction();
}

long long UcdIngestor::get_or_insert_general_category(const std::string& short_code, const std::string& description) {
    if (m_general_category_cache.count(short_code)) {
        return m_general_category_cache[short_code];
    }

    GeneralCategory gc_model(short_code, description);
    m_db_connection->upsert(
        gc_model.get_table_name(),
        gc_model.to_db_map(),
        "general_categories_short_code_key", // Name of the unique constraint
        gc_model.get_update_columns()
    );

    // Retrieve the ID after upsert (if it was an insert)
    auto result = m_db_connection->execute_query(
        "SELECT id FROM general_categories WHERE short_code = '" + short_code + "';"
    );
    if (result->is_empty()) {
        throw std::runtime_error("Failed to retrieve ID for GeneralCategory: " + short_code);
    }
    long long id = std::stoll(result->at(0, "id"));
    m_general_category_cache[short_code] = id;
    return id;
}

long long UcdIngestor::get_or_insert_combining_class(int value, const std::string& description) {
    if (m_combining_class_cache.count(value)) {
        return m_combining_class_cache[value];
    }

    CombiningClass cc_model(value, description);
    m_db_connection->upsert(
        cc_model.get_table_name(),
        cc_model.to_db_map(),
        "combining_classes_value_key", // Name of the unique constraint
        cc_model.get_update_columns()
    );

    auto result = m_db_connection->execute_query(
        "SELECT id FROM combining_classes WHERE value = " + std::to_string(value) + ";"
    );
    if (result->is_empty()) {
        throw std::runtime_error("Failed to retrieve ID for CombiningClass: " + std::to_string(value));
    }
    long long id = std::stoll(result->at(0, "id"));
    m_combining_class_cache[value] = id;
    return id;
}

long long UcdIngestor::get_or_insert_bidi_class(const std::string& short_code, const std::string& description) {
    if (m_bidi_class_cache.count(short_code)) {
        return m_bidi_class_cache[short_code];
    }

    BidiClass bc_model(short_code, description);
    m_db_connection->upsert(
        bc_model.get_table_name(),
        bc_model.to_db_map(),
        "bidi_classes_short_code_key", // Name of the unique constraint
        bc_model.get_update_columns()
    );

    auto result = m_db_connection->execute_query(
        "SELECT id FROM bidi_classes WHERE short_code = '" + short_code + "';"
    );
    if (result->is_empty()) {
        throw std::runtime_error("Failed to retrieve ID for BidiClass: " + short_code);
    }
    long long id = std::stoll(result->at(0, "id"));
    m_bidi_class_cache[short_code] = id;
    return id;
}

long long UcdIngestor::get_or_insert_numeric_type(const std::string& type_name) {
    if (m_numeric_type_cache.count(type_name)) {
        return m_numeric_type_cache[type_name];
    }

    NumericType nt_model(type_name);
    m_db_connection->upsert(
        nt_model.get_table_name(),
        nt_model.to_db_map(),
        "numeric_types_type_name_key", // Name of the unique constraint
        nt_model.get_update_columns()
    );

    auto result = m_db_connection->execute_query(
        "SELECT id FROM numeric_types WHERE type_name = '" + type_name + "';"
    );
    if (result->is_empty()) {
        throw std::runtime_error("Failed to retrieve ID for NumericType: " + type_name);
    }
    long long id = std::stoll(result->at(0, "id"));
    m_numeric_type_cache[type_name] = id;
    return id;
}

long long UcdIngestor::get_or_insert_property(const std::string& short_name, const std::string& long_name, const std::string& category) {
    if (m_property_cache.count(short_name)) {
        return m_property_cache[short_name];
    }

    Property prop_model(short_name, long_name, category);
    m_db_connection->upsert(
        prop_model.get_table_name(),
        prop_model.to_db_map(),
        "properties_short_name_key", // Name of the unique constraint
        prop_model.get_update_columns()
    );

    auto result = m_db_connection->execute_query(
        "SELECT id FROM properties WHERE short_name = '" + short_name + "';"
    );
    if (result->is_empty()) {
        throw std::runtime_error("Failed to retrieve ID for Property: " + short_name);
    }
    long long id = std::stoll(result->at(0, "id"));
    m_property_cache[short_name] = id;
    return id;
}

void UcdIngestor::load_blocks_from_db() {
    auto result = m_db_connection->execute_query("SELECT id, start_code_hex, end_code_hex, name FROM blocks ORDER BY start_code_int;");
    if (!result->is_empty()) {
        m_blocks_cache.clear(); // Clear existing cache if reloading
        m_blocks_cache.reserve(result->size());
        for (size_t i = 0; i < result->size(); ++i) {
            auto block = std::make_unique<Block>(result->at(i, "start_code_hex"), result->at(i, "end_code_hex"), result->at(i, "name"));
            block->set_id(std::stoll(result->at(i, "id")));
            m_blocks_cache.push_back(std::move(block));
        }
        std::sort(m_blocks_cache.begin(), m_blocks_cache.end(), [](const std::unique_ptr<Block>& a, const std::unique_ptr<Block>& b){
            return a->start_code_int < b->start_code_int;
        });
    }
}

void UcdIngestor::load_ages_from_db() {
    auto result = m_db_connection->execute_query("SELECT id, start_code_hex, end_code_hex, version, comment FROM ages ORDER BY start_code_int;");
    if (!result->is_empty()) {
        m_ages_cache.clear(); // Clear existing cache if reloading
        m_ages_cache.reserve(result->size());
        for (size_t i = 0; i < result->size(); ++i) {
            std::optional<std::string> comment_val;
            // Check if comment is NULL in DB
            if (!result->at(i, "comment").empty() && !result->at(i, "comment").is_null()) {
                 comment_val = result->at(i, "comment");
            }

            auto age = std::make_unique<Age>(
                result->at(i, "start_code_hex"),
                result->at(i, "end_code_hex"),
                result->at(i, "version"),
                comment_val
            );
            age->set_id(std::stoll(result->at(i, "id")));
            m_ages_cache.push_back(std::move(age));
        }
        std::sort(m_ages_cache.begin(), m_ages_cache.end(), [](const std::unique_ptr<Age>& a, const std::unique_ptr<Age>& b){
            return a->start_code_int < b->start_code_int;
        });
    }
}

void UcdIngestor::load_properties_from_db() {
    auto result = m_db_connection->execute_query("SELECT id, short_name FROM properties;");
    if (!result->is_empty()) {
        for (size_t i = 0; i < result->size(); ++i) {
            m_property_cache[result->at(i, "short_name")] = std::stoll(result->at(i, "id"));
        }
    }
}


long long UcdIngestor::find_block_id_for_code_point(const std::string& code_point_hex) {
    long long cp_int = hex_to_int(code_point_hex);
    // Use std::upper_bound with a custom comparator
    auto it = std::upper_bound(m_blocks_cache.begin(), m_blocks_cache.end(), cp_int, 
        [](long long val, const std::unique_ptr<Block>& block_ptr) {
            return val < block_ptr->start_code_int;
        });

    if (it != m_blocks_cache.begin()) {
        --it; // Move back to the potential block containing cp_int
        if (cp_int >= (*it)->start_code_int && cp_int <= (*it)->end_code_int) {
            return (*it)->get_id();
        }
    }
    // Return ID for a special 'No_Block' or 0 if not found and DB allows NULL FK
    return 0; // Indicating no block found or 'No_Block'
}

long long UcdIngestor::find_age_id_for_code_point(const std::string& code_point_hex) {
    long long cp_int = hex_to_int(code_point_hex);
    // Use std::upper_bound with a custom comparator
    auto it = std::upper_bound(m_ages_cache.begin(), m_ages_cache.end(), cp_int, 
        [](long long val, const std::unique_ptr<Age>& age_ptr) {
            return val < age_ptr->start_code_int;
        });

    if (it != m_ages_cache.begin()) {
        --it; // Move back to the potential age range containing cp_int
        if (cp_int >= (*it)->start_code_int && cp_int <= (*it)->end_code_int) {
            return (*it)->get_id();
        }
    }
    // Return ID for a special 'Unassigned' Age or 0 if not found and DB allows NULL FK
    return 0;
}

void UcdIngestor::ingest_blocks_data(const std::string& filepath) {
    UcdFileReader<Block> reader(std::make_unique<BlocksParser>());
    reader.open(filepath);
    
    m_db_connection->begin_transaction();
    int count = 0;
    while (reader.has_next()) {
        auto block = reader.read_next();
        if (!block) {
            std::cerr << "Warning: Failed to parse a line in Blocks.txt." << std::endl;
            continue;
        }
        m_db_connection->upsert(
            block->get_table_name(),
            block->to_db_map(),
            "blocks_name_key", // Name of the unique constraint for blocks
            block->get_update_columns()
        );
        count++;
    }
    m_db_connection->commit_transaction();
    load_blocks_from_db(); // Reload cache with newly ingested blocks
    std::cout << "Ingested " << count << " blocks." << std::endl;
}

void UcdIngestor::ingest_derived_age_data(const std::string& filepath) {
    UcdFileReader<Age> reader(std::make_unique<DerivedAgeParser>());
    reader.open(filepath);
    
    m_db_connection->begin_transaction();
    int count = 0;
    while (reader.has_next()) {
        auto age = reader.read_next();
        if (!age) {
            std::cerr << "Warning: Failed to parse a line in DerivedAge.txt." << std::endl;
            continue;
        }
        // For ages, the unique key is (start_code_int, end_code_int, version)
        // This is explicitly named 'ages_start_code_int_end_code_int_version_key' in create_tables.sql
        m_db_connection->upsert(
            age->get_table_name(),
            age->to_db_map(),
            "ages_start_code_int_end_code_int_version_key", // Custom unique constraint name
            age->get_update_columns()
        );
        count++;
    }
    m_db_connection->commit_transaction();
    load_ages_from_db(); // Reload cache with newly ingested ages
    std::cout << "Ingested " << count << " age ranges." << std::endl;
}

// Dedicated parser for PropertyAliases.txt
class PropertyAliasesParser : public UcdLineParserBase, public IDataParser<Property> {
public:
    std::unique_ptr<Property> parse_line(const std::string& preprocessed_line) const override {
        std::vector<std::string> fields = split(preprocessed_line, ';');
        if (fields.size() < 2) {
            throw std::runtime_error("Invalid line format for PropertyAliases.txt: " + preprocessed_line);
        }

        std::string short_name = trim(fields[0]);
        std::string long_name = trim(fields[1]);
        std::string category = "Miscellaneous"; // Default generic category

        // Basic inference for common categories. For full accuracy,
        // the section headers in PropertyAliases.txt would need to be parsed.
        if (long_name.find("Numeric") != std::string::npos || short_name == "nv") category = "Numeric";
        else if (long_name.find("Case_Folding") != std::string::npos || long_name.find("Mapping") != std::string::npos) category = "String";
        else if (long_name == "Age" || long_name == "Block" || long_name == "Script") category = "Catalog";
        else if (long_name.find("_Class") != std::string::npos || long_name.find("_Type") != std::string::npos || long_name.find("Break") != std::string::npos || short_name == "hst" || short_name == "ea" || short_name == "gc" || short_name == "nt" || short_name == "vo") category = "Enumerated";
        else category = "Binary"; // Most remaining are binary properties

        return std::make_unique<Property>(short_name, long_name, category);
    }
};

void UcdIngestor::ingest_property_aliases_data(const std::string& filepath) {
    UcdFileReader<Property> reader(std::make_unique<PropertyAliasesParser>());
    reader.open(filepath);
    
    m_db_connection->begin_transaction();
    int count = 0;
    while (reader.has_next()) {
        auto prop = reader.read_next();
        if (!prop) {
            std::cerr << "Warning: Failed to parse a line in PropertyAliases.txt." << std::endl;
            continue;
        }
        get_or_insert_property(prop->short_name, prop->long_name, prop->category);
        count++;
    }
    m_db_connection->commit_transaction();
    load_properties_from_db(); // Reload cache with newly ingested properties
    std::cout << "Ingested " << count << " property aliases." << std::endl;
}

void UcdIngestor::ingest_unicode_data(const std::string& filepath) {
    UcdFileReader<CodePoint> reader(std::make_unique<UnicodeDataParser>());
    reader.open(filepath);

    m_db_connection->begin_transaction();
    int count = 0;
    while (reader.has_next()) {
        auto cp = reader.read_next();
        if (!cp) {
            std::cerr << "Warning: Failed to parse a line in UnicodeData.txt." << std::endl;
            continue;
        }

        // Resolve FKs using raw codes/values from parsed CodePoint object
        cp->general_category_fk_id = get_or_insert_general_category(cp->general_category_code_raw, ""); 
        cp->combining_class_fk_id = get_or_insert_combining_class(cp->combining_class_value_raw, ""); 
        cp->bidi_class_fk_id = get_or_insert_bidi_class(cp->bidi_class_code_raw, ""); 
        cp->numeric_type_fk_id = get_or_insert_numeric_type(cp->numeric_type_raw.value_or("")); // "" if optional is empty

        // Resolve Block and Age IDs using the cached ranges
        cp->block_fk_id = find_block_id_for_code_point(cp->code_point_id);
        cp->age_fk_id = find_age_id_for_code_point(cp->code_point_id);

        m_db_connection->upsert(
            cp->get_table_name(),
            cp->to_db_map(),
            cp->get_primary_key_column(), // "code_point_id"
            cp->get_update_columns()
        );
        count++;
        if (count % 10000 == 0) { // Log every 10,000 code points
            std::cout << "Ingested " << count << " code points..." << std::endl;
        }
    }
    m_db_connection->commit_transaction();
    std::cout << "Ingested " << count << " Unicode code points." << std::endl;
}

void UcdIngestor::run_ingestion_workflow(
    const std::string& unicode_data_path,
    const std::string& blocks_path,
    const std::string& derived_age_path,
    const std::string& property_aliases_path
) {
    try {
        initialize_database(); // Connects and loads initial caches
        
        std::cout << "Ingesting Blocks data..." << std::endl;
        ingest_blocks_data(blocks_path);

        std::cout << "Ingesting DerivedAge data..." << std::endl;
        ingest_derived_age_data(derived_age_path);

        std::cout << "Ingesting Property Aliases data..." << std::endl;
        ingest_property_aliases_data(property_aliases_path);

        std::cout << "Ingesting Unicode Data..." << std::endl;
        ingest_unicode_data(unicode_data_path);

        std::cout << "Seeding Atoms from UCD..." << std::endl;
        seed_atoms_from_ucd();

        std::cout << "UCD ingestion completed successfully." << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "Error during UCD ingestion: " << e.what() << std::endl;
        if (m_db_connection) {
            try {
                // Check if a transaction is active before attempting to rollback
                // (m_tx is private in PgConnection, need to check via a public method if implemented, or rely on exception propagation)
                // For simplicity, assuming if an error occurs during an ingetsion_data call, a transaction was active and should be rolled back.
                // A more robust solution might expose `is_transaction_active()`
                std::cerr << "Attempting to rollback transaction due to error." << std::endl;
                m_db_connection->rollback_transaction();
                std::cerr << "Transaction rolled back." << std::endl;
            } catch (const std::exception& rb_e) {
                std::cerr << "Error during rollback: " << rb_e.what() << std::endl;
            }
        }
    }
}

void UcdIngestor::seed_atoms_from_ucd() {
    using namespace hartonomous::unicode;
    
    std::cout << "Starting Atom Seeding (0x000000 to 0x10FFFF)..." << std::endl;
    
    // Batch size for bulk inserts
    const int BATCH_SIZE = 1000;
    std::vector<uint32_t> batch_codepoints;
    batch_codepoints.reserve(BATCH_SIZE);

    m_db_connection->begin_transaction();

    // Iterate entire Unicode space
    for (uint32_t cp = 0; cp <= 0x10FFFF; ++cp) {
        // Skip surrogates? Typically yes, as they aren't scalar values.
        // U+D800 to U+DFFF
        if (cp >= 0xD800 && cp <= 0xDFFF) continue;

        batch_codepoints.push_back(cp);

        if (batch_codepoints.size() >= BATCH_SIZE) {
            // Process Batch
            auto projections = CodepointProjection::project_batch(batch_codepoints);
            
            std::stringstream sql_phys;
            std::stringstream sql_atoms;
            
            sql_phys << "INSERT INTO \"Physicalities\" (\"Id\", \"HilbertIndex\", \"Centroid\") VALUES ";
            sql_atoms << "INSERT INTO \"Atoms\" (\"Id\", \"PhysicalityId\", \"Codepoint\") VALUES ";

            bool first = true;
            for (const auto& proj : projections) {
                if (!first) {
                    sql_phys << ",";
                    sql_atoms << ",";
                }
                first = false;

                // --- Hash to Hex Helper ---
                auto to_uuid = [&](const std::array<uint8_t, 32>& hash) {
                    std::stringstream ss;
                    ss << std::hex << std::setfill('0');
                    for (int i = 0; i < 16; ++i) { // Use first 16 bytes for UUID
                        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
                        ss << std::setw(2) << static_cast<int>(hash[i]);
                    }
                    return ss.str();
                };

                // Use Atom Hash as UUID for Atom
                std::string atom_uuid = to_uuid(proj.hash);
                // For base Atoms, Physicality ID is the same as Atom ID (1:1 mapping)
                // This simplifies lookup: given an Atom ID, we know its Physicality ID.
                std::string phys_uuid = atom_uuid; 

                // --- SQL Generation ---
                // Physicality
                // Note: Centroid is Vec4. We store as array literal for now.
                sql_phys << "('" << phys_uuid << "', " 
                         << proj.hilbert_index << ", "
                         << "'{" << proj.s3_position[0] << "," << proj.s3_position[1] << "," 
                         << proj.s3_position[2] << "," << proj.s3_position[3] << "}')"; // Postgres array syntax {}
                
                // Atom
                sql_atoms << "('" << atom_uuid << "', '" 
                          << phys_uuid << "', " 
                          << proj.codepoint << ")";
            }
            
            sql_phys << " ON CONFLICT (\"Id\") DO NOTHING";
            sql_atoms << " ON CONFLICT (\"Id\") DO NOTHING";

            m_db_connection->execute_query(sql_phys.str());
            m_db_connection->execute_query(sql_atoms.str());

            batch_codepoints.clear();
            
            if (cp % 10000 == 0) {
                std::cout << "Seeded " << cp << " atoms..." << std::endl;
            }
        }
    }
    
    // Process remaining batch
    if (!batch_codepoints.empty()) {
        auto projections = CodepointProjection::project_batch(batch_codepoints);
        std::stringstream sql_phys;
        std::stringstream sql_atoms;
        sql_phys << "INSERT INTO \"Physicalities\" (\"Id\", \"HilbertIndex\", \"Centroid\") VALUES ";
        sql_atoms << "INSERT INTO \"Atoms\" (\"Id\", \"PhysicalityId\", \"Codepoint\") VALUES ";

        bool first = true;
        for (const auto& proj : projections) {
            if (!first) { sql_phys << ","; sql_atoms << ","; }
            first = false;
            
            auto to_uuid = [&](const std::array<uint8_t, 32>& hash) {
                std::stringstream ss;
                ss << std::hex << std::setfill('0');
                for (int i = 0; i < 16; ++i) {
                    if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
                    ss << std::setw(2) << static_cast<int>(hash[i]);
                }
                return ss.str();
            };

            std::string atom_uuid = to_uuid(proj.hash);
            std::string phys_uuid = atom_uuid; 

            sql_phys << "('" << phys_uuid << "', " << proj.hilbert_index << ", "
                     << "'{" << proj.s3_position[0] << "," << proj.s3_position[1] << "," 
                     << proj.s3_position[2] << "," << proj.s3_position[3] << "}')";
            
            sql_atoms << "('" << atom_uuid << "', '" << phys_uuid << "', " << proj.codepoint << ")";
        }
        
        sql_phys << " ON CONFLICT (\"Id\") DO NOTHING";
        sql_atoms << " ON CONFLICT (\"Id\") DO NOTHING";

        m_db_connection->execute_query(sql_phys.str());
        m_db_connection->execute_query(sql_atoms.str());
    }

    m_db_connection->commit_transaction();
    std::cout << "Atom Seeding Complete." << std::endl;
}
