-- create_tables.sql
-- Connect to the UCD database as ucd_user before running this script:
-- psql -h localhost -p 5432 -U ucd_user -d UCD -f create_tables.sql

-- Drop tables in reverse order of dependency if they exist, for clean recreation
DROP TABLE IF EXISTS code_point_binary_properties CASCADE;
DROP TABLE IF EXISTS code_point_string_properties CASCADE;
DROP TABLE IF EXISTS code_points CASCADE;
DROP TABLE IF EXISTS properties CASCADE;
DROP TABLE IF EXISTS general_categories CASCADE;
DROP TABLE IF EXISTS combining_classes CASCADE;
DROP TABLE IF EXISTS bidi_classes CASCADE;
DROP TABLE IF EXISTS numeric_types CASCADE;
DROP TABLE IF EXISTS blocks CASCADE;
DROP TABLE IF EXISTS ages CASCADE;


-- Table for Unicode Blocks (from Blocks.txt)
CREATE TABLE blocks (
    id SERIAL PRIMARY KEY,
    start_code_hex TEXT NOT NULL,
    end_code_hex TEXT NOT NULL,
    start_code_int BIGINT NOT NULL, -- Use BIGINT for code points beyond 2^31-1
    end_code_int BIGINT NOT NULL,   -- Use BIGINT for code points beyond 2^31-1
    name TEXT NOT NULL UNIQUE -- Unique constraint for upsert conflict target
);

-- Table for Unicode Age (from DerivedAge.txt)
CREATE TABLE ages (
    id SERIAL PRIMARY KEY,
    start_code_hex TEXT NOT NULL,
    end_code_hex TEXT NOT NULL,
    start_code_int BIGINT NOT NULL,
    end_code_int BIGINT NOT NULL,
    version TEXT NOT NULL, -- e.g., '1.1', '2.0', '15.0'
    comment TEXT, -- Optional
    -- Composite unique constraint for upsert conflict target
    UNIQUE (start_code_int, end_code_int, version)
);

-- Lookup table for General Categories (from PropertyAliases.txt, UnicodeData.txt)
CREATE TABLE general_categories (
    id SERIAL PRIMARY KEY,
    short_code TEXT NOT NULL UNIQUE, -- Unique constraint for upsert conflict target
    description TEXT
);

-- Lookup table for Combining Classes (from UnicodeData.txt)
CREATE TABLE combining_classes (
    id SERIAL PRIMARY KEY,
    value INTEGER NOT NULL UNIQUE, -- Unique constraint for upsert conflict target
    description TEXT
);

-- Lookup table for Bidirectional Classes (from PropertyAliases.txt, UnicodeData.txt)
CREATE TABLE bidi_classes (
    id SERIAL PRIMARY KEY,
    short_code TEXT NOT NULL UNIQUE, -- Unique constraint for upsert conflict target
    description TEXT
);

-- Lookup table for Numeric Types (from UnicodeData.txt)
CREATE TABLE numeric_types (
    id SERIAL PRIMARY KEY,
    type_name TEXT NOT NULL UNIQUE -- Unique constraint for upsert conflict target
);

-- Table for Unicode Property Aliases (from PropertyAliases.txt)
CREATE TABLE properties (
    id SERIAL PRIMARY KEY,
    short_name TEXT NOT NULL UNIQUE, -- Unique constraint for upsert conflict target
    long_name TEXT NOT NULL UNIQUE,
    category TEXT NOT NULL -- e.g., 'Numeric', 'String', 'Binary', 'Enumerated', 'Catalog', 'Miscellaneous'
);

-- Table for Unicode Code Points (core data from UnicodeData.txt)
CREATE TABLE code_points (
    code_point_id TEXT PRIMARY KEY, -- Hexadecimal code point (e.g., '0041', '1F600'), acts as unique identifier
    name TEXT NOT NULL,
    general_category_id INTEGER REFERENCES general_categories(id),
    combining_class_id INTEGER REFERENCES combining_classes(id),
    bidi_class_id INTEGER REFERENCES bidi_classes(id),
    decomposition_mapping TEXT, -- Raw string, can be parsed further if needed
    numeric_type_id INTEGER REFERENCES numeric_types(id),
    numeric_value_decimal BIGINT, -- Can be large
    numeric_value_digit BIGINT,   -- Can be large
    numeric_value_numeric TEXT, -- For fractions like "1/2"
    bidi_mirrored BOOLEAN,
    unicode_1_name TEXT,
    iso_comment TEXT,
    simple_uppercase_mapping TEXT, -- Hexadecimal code point
    simple_lowercase_mapping TEXT, -- Hexadecimal code point
    simple_titlecase_mapping TEXT, -- Hexadecimal code point
    block_id INTEGER REFERENCES blocks(id),
    age_id INTEGER REFERENCES ages(id)
);

-- For binary properties (like Alphabetic, Bidi_Control, etc.)
CREATE TABLE code_point_binary_properties (
    code_point_id TEXT REFERENCES code_points(code_point_id) ON DELETE CASCADE,
    property_id INTEGER REFERENCES properties(id) ON DELETE CASCADE,
    value BOOLEAN NOT NULL DEFAULT TRUE, -- Binary properties are usually true if present
    PRIMARY KEY (code_point_id, property_id) -- Composite unique key for upsert
);

-- To handle more complex properties or future properties dynamically.
-- For example, for String Properties like FC_NFKC.
CREATE TABLE code_point_string_properties (
    code_point_id TEXT REFERENCES code_points(code_point_id) ON DELETE CASCADE,
    property_id INTEGER REFERENCES properties(id) ON DELETE CASCADE,
    value TEXT NOT NULL,
    PRIMARY KEY (code_point_id, property_id) -- Composite unique key for upsert
);

-- Indexes for performance
CREATE INDEX idx_code_points_general_category ON code_points (general_category_id);
CREATE INDEX idx_code_points_combining_class ON code_points (combining_class_id);
CREATE INDEX idx_code_points_bidi_class ON code_points (bidi_class_id);
CREATE INDEX idx_code_points_numeric_type ON code_points (numeric_type_id);
CREATE INDEX idx_code_points_block ON code_points (block_id);
CREATE INDEX idx_code_points_age ON code_points (age_id);

CREATE INDEX idx_blocks_start_end_int ON blocks (start_code_int, end_code_int);
CREATE INDEX idx_ages_start_end_int ON ages (start_code_int, end_code_int);

\echo 'All UCD tables created successfully.'
