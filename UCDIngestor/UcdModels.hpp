// UcdModels.hpp
#ifndef UCD_MODELS_HPP
#define UCD_MODELS_HPP

#include "IDataModel.hpp"
#include <string>
#include <vector>
#include <optional>
#include <map>
#include <stdexcept> // For std::runtime_error
#include <algorithm> // For std::remove_if
#include <cctype>    // For std::isspace

// Helper to convert hex string to int
inline long long hex_to_int(const std::string& hex_str) {
    if (hex_str.empty()) return 0;
    // Remove "U+" prefix if present, though UCD files usually don't have it
    std::string clean_hex = hex_str;
    size_t pos = clean_hex.find("U+");
    if (pos == 0) {
        clean_hex.erase(0, 2);
    }
    // Remove any leading/trailing whitespace
    clean_hex.erase(0, clean_hex.find_first_not_of(" \t\n\r\f\v"));
    clean_hex.erase(clean_hex.find_last_not_of(" \t\n\r\f\v") + 1);

    if (clean_hex.empty()) return 0;

    try {
        return std::stoll(clean_hex, nullptr, 16);
    } catch (const std::exception& e) {
        throw std::runtime_error("Failed to convert hex string '" + hex_str + "' to int: " + e.what());
    }
}

// Helper to convert string to bool, handling various string representations
inline bool string_to_bool(const std::string& s) {
    std::string lower_s = s;
    std::transform(lower_s.begin(), lower_s.end(), lower_s.begin(),
                   [](unsigned char c){ return std::tolower(c); });
    return (lower_s == "true" || lower_s == "t" || lower_s == "1" || lower_s == "yes" || lower_s == "y");
}


// Base class for models that do NOT have a serial 'id' primary key
class NonSerialModelBase : public IDataModel {
protected:
    long long m_id = 0; // Not used as PK, but might be set if retrieved from DB as FK for other tables
public:
    long long get_id() const override { return m_id; }
    void set_id(long long id) override { m_id = id; }
    bool has_serial_id() const override { return false; } // This model does not use a serial id as PK
};


// -----------------------------------------------------------------------------
// Lookup Models (have SERIAL 'id' but also a natural UNIQUE key for upsert)
// -----------------------------------------------------------------------------

struct GeneralCategory : public ModelBase {
    std::string short_code;
    std::string description;

    GeneralCategory(const std::string& sc = "", const std::string& desc = "")
        : short_code(sc), description(desc) {}

    std::string get_table_name() const override { return "general_categories"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"short_code", short_code},
            {"description", description}
        };
    }
    // Conflict on 'short_code' unique constraint
    std::string get_primary_key_column() const override { return "short_code"; }
    std::string get_primary_key_value() const override { return short_code; }
    std::vector<std::string> get_update_columns() const override {
        return {"description"}; // Only description can be updated on conflict
    }
};

struct CombiningClass : public ModelBase {
    int value;
    std::string description;

    CombiningClass(int val = 0, const std::string& desc = "")
        : value(val), description(desc) {}

    std::string get_table_name() const override { return "combining_classes"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"value", std::to_string(value)},
            {"description", description}
        };
    }
    // Conflict on 'value' unique constraint
    std::string get_primary_key_column() const override { return "value"; }
    std::string get_primary_key_value() const override { return std::to_string(value); }
    std::vector<std::string> get_update_columns() const override {
        return {"description"};
    }
};

struct BidiClass : public ModelBase {
    std::string short_code;
    std::string description;

    BidiClass(const std::string& sc = "", const std::string& desc = "")
        : short_code(sc), description(desc) {}

    std::string get_table_name() const override { return "bidi_classes"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"short_code", short_code},
            {"description", description}
        };
    }
    // Conflict on 'short_code' unique constraint
    std::string get_primary_key_column() const override { return "short_code"; }
    std::string get_primary_key_value() const override { return short_code; }
    std::vector<std::string> get_update_columns() const override {
        return {"description"};
    }
};

struct NumericType : public ModelBase {
    std::string type_name;

    NumericType(const std::string& tn = "") : type_name(tn) {}

    std::string get_table_name() const override { return "numeric_types"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"type_name", type_name}
        };
    }
    // Conflict on 'type_name' unique constraint
    std::string get_primary_key_column() const override { return "type_name"; }
    std::string get_primary_key_value() const override { return type_name; }
    std::vector<std::string> get_update_columns() const override {
        return {}; // No other columns to update if type_name is the unique key
    }
};

struct Property : public ModelBase {
    std::string short_name;
    std::string long_name;
    std::string category;

    Property(const std::string& sn = "", const std::string& ln = "", const std::string& cat = "")
        : short_name(sn), long_name(ln), category(cat) {}

    std::string get_table_name() const override { return "properties"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"short_name", short_name},
            {"long_name", long_name},
            {"category", category}
        };
    }
    // Conflict on 'short_name' unique constraint
    std::string get_primary_key_column() const override { return "short_name"; }
    std::string get_primary_key_value() const override { return short_name; }
    std::vector<std::string> get_update_columns() const override {
        return {"long_name", "category"};
    }
};


// -----------------------------------------------------------------------------
// Range Models (have SERIAL 'id' for FK, but typically inserted by range)
// -----------------------------------------------------------------------------

struct Block : public ModelBase {
    std::string start_code_hex;
    std::string end_code_hex;
    std::string name;
    long long start_code_int;
    long long end_code_int;

    Block(const std::string& start_hex = "", const std::string& end_hex = "", const std::string& n = "")
        : start_code_hex(start_hex), end_code_hex(end_hex), name(n) {
        start_code_int = hex_to_int(start_code_hex);
        end_code_int = hex_to_int(end_code_hex);
    }

    std::string get_table_name() const override { return "blocks"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"start_code_hex", start_code_hex},
            {"end_code_hex", end_code_hex},
            {"start_code_int", std::to_string(start_code_int)},
            {"end_code_int", std::to_string(end_code_int)},
            {"name", name}
        };
    }
    // Conflict on 'name' unique constraint
    std::string get_primary_key_column() const override { return "name"; }
    std::string get_primary_key_value() const override { return name; }
    std::vector<std::string> get_update_columns() const override {
        return {"start_code_hex", "end_code_hex", "start_code_int", "end_code_int"};
    }
};

struct Age : public ModelBase {
    std::string start_code_hex;
    std::string end_code_hex;
    std::string version;
    std::optional<std::string> comment; // Optional field
    long long start_code_int;
    long long end_code_int;

    Age(const std::string& start_hex = "", const std::string& end_hex = "", const std::string& ver = "", const std::optional<std::string>& c = std::nullopt)
        : start_code_hex(start_hex), end_code_hex(end_hex), version(ver), comment(c) {
        start_code_int = hex_to_int(start_code_hex);
        end_code_int = hex_to_int(end_code_hex);
    }

    std::string get_table_name() const override { return "ages"; }
    std::map<std::string, std::string> to_db_map() const override {
        std::map<std::string, std::string> map_data = {
            {"start_code_hex", start_code_hex},
            {"end_code_hex", end_code_hex},
            {"start_code_int", std::to_string(start_code_int)},
            {"end_code_int", std::to_string(end_code_int)},
            {"version", version}
        };
        if (comment) map_data["comment"] = *comment;
        return map_data;
    }
    // Composite PK for upserting
    std::string get_primary_key_column() const override { return "start_code_int, end_code_int, version"; } // Composite key for ON CONFLICT
    // For a composite key, get_primary_key_value is tricky. The upsert method in PgConnection.cpp needs
    // to know the *name* of the UNIQUE constraint, not necessarily the values.
    // The PgConnection.cpp upsert uses `conflict_target` for the constraint name.
    // So, 'id' is still the actual PK, but upsert targets the composite UNIQUE constraint.
    std::string get_primary_key_value() const override { return std::to_string(m_id); } // Dummy, not used for upsert conflict
    std::vector<std::string> get_update_columns() const override {
        return {"start_code_hex", "end_code_hex", "version", "comment"};
    }
};

// -----------------------------------------------------------------------------
// Core Code Point Model (references many lookup/range tables)
// UnicodeDataParser needs to be updated to populate these raw string fields.
// -----------------------------------------------------------------------------

struct CodePoint : public NonSerialModelBase { // PK is code_point_id (TEXT), not serial 'id'
    std::string code_point_id; // Hex string, e.g., "0041"
    std::string name;
    
    // Raw fields from UnicodeData.txt that need lookup
    std::string general_category_code_raw;
    int combining_class_value_raw = 0;
    std::string bidi_class_code_raw;
    std::optional<std::string> numeric_type_raw; // Can be empty string

    // Foreign key IDs
    long long general_category_fk_id = 0;
    long long combining_class_fk_id = 0;
    long long bidi_class_fk_id = 0;
    long long numeric_type_fk_id = 0;
    long long block_fk_id = 0;
    long long age_fk_id = 0;


    // Directly insertable fields
    std::optional<std::string> decomposition_mapping; // Can be empty
    std::optional<long long> numeric_value_decimal;
    std::optional<long long> numeric_value_digit;
    std::optional<std::string> numeric_value_numeric;
    std::optional<bool> bidi_mirrored;
    std::optional<std::string> unicode_1_name;
    std::optional<std::string> iso_comment;
    std::optional<std::string> simple_uppercase_mapping;
    std::optional<std::string> simple_lowercase_mapping;
    std::optional<std::string> simple_titlecase_mapping;


    CodePoint(const std::string& cp_id = "", const std::string& n = "")
        : code_point_id(cp_id), name(n) {}

    std::string get_table_name() const override { return "code_points"; }
    std::map<std::string, std::string> to_db_map() const override {
        std::map<std::string, std::string> map_data = {
            {"code_point_id", code_point_id},
            {"name", name}
        };
        // Only include FKs if they are valid (non-zero)
        if (general_category_fk_id != 0) map_data["general_category_id"] = std::to_string(general_category_fk_id);
        if (combining_class_fk_id != 0) map_data["combining_class_id"] = std::to_string(combining_class_fk_id);
        if (bidi_class_fk_id != 0) map_data["bidi_class_id"] = std::to_string(bidi_class_fk_id);
        if (block_fk_id != 0) map_data["block_id"] = std::to_string(block_fk_id);
        if (age_fk_id != 0) map_data["age_id"] = std::to_string(age_fk_id);

        // Optional fields
        if (decomposition_mapping) map_data["decomposition_mapping"] = *decomposition_mapping;
        if (numeric_type_fk_id != 0) map_data["numeric_type_id"] = std::to_string(numeric_type_fk_id);
        if (numeric_value_decimal) map_data["numeric_value_decimal"] = std::to_string(*numeric_value_decimal);
        if (numeric_value_digit) map_data["numeric_value_digit"] = std::to_string(*numeric_value_digit);
        if (numeric_value_numeric) map_data["numeric_value_numeric"] = *numeric_value_numeric;
        if (bidi_mirrored) map_data["bidi_mirrored"] = (*bidi_mirrored ? "TRUE" : "FALSE");
        if (unicode_1_name) map_data["unicode_1_name"] = *unicode_1_name;
        if (iso_comment) map_data["iso_comment"] = *iso_comment;
        if (simple_uppercase_mapping) map_data["simple_uppercase_mapping"] = *simple_uppercase_mapping;
        if (simple_lowercase_mapping) map_data["simple_lowercase_mapping"] = *simple_lowercase_mapping;
        if (simple_titlecase_mapping) map_data["simple_titlecase_mapping"] = *simple_titlecase_mapping;
        return map_data;
    }
    std::string get_primary_key_column() const override { return "code_point_id"; }
    std::string get_primary_key_value() const override { return code_point_id; }
    // All fields except PK can be updated for CodePoint
    std::vector<std::string> get_update_columns() const override {
        return {
            "name", "general_category_id", "combining_class_id", "bidi_class_id",
            "decomposition_mapping", "numeric_type_id", "numeric_value_decimal",
            "numeric_value_digit", "numeric_value_numeric", "bidi_mirrored",
            "unicode_1_name", "iso_comment", "simple_uppercase_mapping",
            "simple_lowercase_mapping", "simple_titlecase_mapping", "block_id", "age_id"
        };
    }
    bool has_serial_id() const override { return false; } // CodePoint uses code_point_id as PK, not a serial ID
};

// -----------------------------------------------------------------------------
// Other Property Models (for code_point_binary_properties and code_point_string_properties)
// -----------------------------------------------------------------------------

struct CodePointBinaryProperty : public NonSerialModelBase { // Composite PK, no serial ID
    std::string code_point_id; // FK to code_points
    long long property_fk_id;  // FK to properties
    bool value;

    CodePointBinaryProperty(const std::string& cp_id = "", long long prop_id = 0, bool val = true)
        : code_point_id(cp_id), property_fk_id(prop_id), value(val) {}

    std::string get_table_name() const override { return "code_point_binary_properties"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"code_point_id", code_point_id},
            {"property_id", std::to_string(property_fk_id)},
            {"value", value ? "TRUE" : "FALSE"}
        };
    }
    // Composite PK for upserting
    std::string get_primary_key_column() const override { return "code_point_id, property_id"; } // Used as conflict_target
    std::string get_primary_key_value() const override { return code_point_id + ", " + std::to_string(property_fk_id); } // Not used for conflict_target directly
    std::vector<std::string> get_update_columns() const override {
        return {"value"};
    }
    bool has_serial_id() const override { return false; }
};

struct CodePointStringProperty : public NonSerialModelBase { // Composite PK, no serial ID
    std::string code_point_id; // FK to code_points
    long long property_fk_id;  // FK to properties
    std::string value;

    CodePointStringProperty(const std::string& cp_id = "", long long prop_id = 0, const std::string& val = "")
        : code_point_id(cp_id), property_fk_id(prop_id), value(val) {}

    std::string get_table_name() const override { return "code_point_string_properties"; }
    std::map<std::string, std::string> to_db_map() const override {
        return {
            {"code_point_id", code_point_id},
            {"property_id", std::to_string(property_fk_id)},
            {"value", value}
        };
    }
    // Composite PK for upserting
    std::string get_primary_key_column() const override { return "code_point_id, property_id"; } // Used as conflict_target
    std::string get_primary_key_value() const override { return code_point_id + ", " + std::to_string(property_fk_id); } // Not used for conflict_target directly
    std::vector<std::string> get_update_columns() const override {
        return {"value"};
    }
    bool has_serial_id() const override { return false; }
};

#endif // UCD_MODELS_HPP
