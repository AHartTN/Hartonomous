#ifndef IDATA_MODEL_HPP
#define IDATA_MODEL_HPP

#include <string>
#include <map>
#include <vector>

// Abstract Base Class for all Data Models
class IDataModel {
public:
    virtual ~IDataModel() = default;

    // Returns the table name this model maps to
    virtual std::string get_table_name() const = 0;

    // Returns the Primary Key column name
    virtual std::string get_primary_key_column() const = 0;

    // Returns the Primary Key value as string
    virtual std::string get_primary_key_value() const = 0;

    // Returns a map of column_name -> value for insertion/update
    virtual std::map<std::string, std::string> to_db_map() const = 0;

    // Returns a list of columns to update on conflict (Upsert strategy)
    virtual std::vector<std::string> get_update_columns() const = 0;
};

#endif // IDATA_MODEL_HPP