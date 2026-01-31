// IDataModel.hpp
#ifndef IDATA_MODEL_HPP
#define IDATA_MODEL_HPP

#include <string>
#include <map>
#include <memory>
#include <vector>

// Abstract interface for any data model that can be stored in the DB
class IDataModel {
public:
    virtual ~IDataModel() = default;
    virtual std::string get_table_name() const = 0;
    virtual std::map<std::string, std::string> to_db_map() const = 0; // Convert to map for DB insertion
    virtual std::string get_primary_key_column() const = 0; // For upserts and identification
    virtual std::string get_primary_key_value() const = 0; // For upserts and identification
    virtual std::vector<std::string> get_update_columns() const = 0; // For upserts
    virtual void set_id(long long id) = 0; // For models with SERIAL primary keys
    virtual long long get_id() const = 0;
    virtual bool has_serial_id() const = 0; // Indicates if the model uses a SERIAL 'id' as primary key
};

// Base class for models that have a serial 'id' primary key
class ModelBase : public IDataModel {
protected:
    long long m_id = 0; // Default to 0, indicating not yet in DB
public:
    long long get_id() const override { return m_id; }
    void set_id(long long id) override { m_id = id; }
    bool has_serial_id() const override { return true; } // By default, models have a serial id

    // Default implementations for primary key related methods, to be overridden if PK is not 'id' or not serial
    std::string get_primary_key_column() const override { return "id"; }
    std::string get_primary_key_value() const override { return std::to_string(m_id); }
    std::vector<std::string> get_update_columns() const override {
        // By default, update all columns except the primary key 'id'
        std::vector<std::string> columns;
        for(auto const& [key, val] : to_db_map()) {
            if (key != get_primary_key_column()) {
                columns.push_back(key);
            }
        }
        return columns;
    }
};

#endif // IDATA_MODEL_HPP
