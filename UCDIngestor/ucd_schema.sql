-- ucd_schema.sql
-- Exact UCD Schema based on Unicode Standard Annex #44
-- Tables are normalized where appropriate (Lookups) and flat where efficient (Code Points).

-- 1. CLEANUP
DROP TABLE IF EXISTS code_point_properties CASCADE; -- Generic mess
DROP TABLE IF EXISTS unicode_aliases CASCADE;
DROP TABLE IF EXISTS code_points CASCADE;
DROP TABLE IF EXISTS scripts CASCADE;
DROP TABLE IF EXISTS blocks CASCADE;
DROP TABLE IF EXISTS bidi_classes CASCADE;
DROP TABLE IF EXISTS general_categories CASCADE;
DROP TABLE IF EXISTS derived_ages CASCADE;
DROP TABLE IF EXISTS ucd_versions CASCADE;
DROP TABLE IF EXISTS properties CASCADE;

-- 2. LOOKUP TABLES (Enumerations)

-- General Category (gc): Lu, Ll, Nd, etc.
CREATE TABLE general_categories (
    id SERIAL PRIMARY KEY,
    code VARCHAR(5) NOT NULL UNIQUE, -- The abbreviation (e.g., "Lu")
    description VARCHAR(255)         -- The full name (e.g., "Uppercase_Letter")
);

-- Bidi Class (bc): L, R, AL, etc.
CREATE TABLE bidi_classes (
    id SERIAL PRIMARY KEY,
    code VARCHAR(5) NOT NULL UNIQUE,
    description VARCHAR(255)
);

-- Scripts (sc)
CREATE TABLE scripts (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE, -- "Latin", "Cyrillic"
    iso_code VARCHAR(10)               -- "Latn", "Cyrl" (from PropertyValueAliases)
);

-- Blocks (blk)
CREATE TABLE blocks (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL UNIQUE,
    start_cp INTEGER NOT NULL,
    end_cp INTEGER NOT NULL,
    CHECK (start_cp <= end_cp)
);
CREATE INDEX idx_blocks_range ON blocks(start_cp, end_cp);

-- 3. CORE TABLE: UnicodeData.txt
-- Matches the 15 fields of UnicodeData.txt
CREATE TABLE code_points (
    codepoint INTEGER PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    
    -- Field 2: General Category
    general_category_id INTEGER REFERENCES general_categories(id),
    
    -- Field 3: Canonical Combining Class (0..254)
    combining_class INTEGER DEFAULT 0,
    
    -- Field 4: Bidi Class
    bidi_class_id INTEGER REFERENCES bidi_classes(id),
    
    -- Field 5: Decomposition (Type extracted, Mapping stored)
    decomposition_type VARCHAR(50), -- e.g., "<compat>", "<super>"
    decomposition_mapping VARCHAR(255), -- Space separated hex
    
    -- Fields 6-8: Numeric Values
    numeric_value_decimal INTEGER, -- 0-9
    numeric_value_digit INTEGER,   -- 0-9
    numeric_value_numeric DOUBLE PRECISION, -- e.g., 0.5
    
    -- Field 9: Bidi Mirrored
    bidi_mirrored BOOLEAN DEFAULT FALSE,
    
    -- Field 10: Unicode 1.0 Name (Obsolete but stored)
    unicode_1_name VARCHAR(255),
    
    -- Field 11: ISO Comment
    iso_comment VARCHAR(255),
    
    -- Fields 12-14: Case Mappings (Simple)
    simple_uppercase_mapping INTEGER,
    simple_lowercase_mapping INTEGER,
    simple_titlecase_mapping INTEGER,
    
    -- DERIVED ASSOCIATIONS (Populated via Range Join)
    script_id INTEGER REFERENCES scripts(id),
    block_id INTEGER REFERENCES blocks(id)
);

-- 4. EXTENDED PROPERTIES
-- For PropList.txt (Binary Properties like "White_Space", "Dash")
CREATE TABLE properties (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE code_point_properties (
    codepoint INTEGER REFERENCES code_points(codepoint) ON DELETE CASCADE,
    property_id INTEGER REFERENCES properties(id) ON DELETE CASCADE,
    PRIMARY KEY (codepoint, property_id)
);

-- Indexes for performance
CREATE INDEX idx_cp_gc ON code_points(general_category_id);
CREATE INDEX idx_cp_bc ON code_points(bidi_class_id);
CREATE INDEX idx_cp_script ON code_points(script_id);
CREATE INDEX idx_cp_block ON code_points(block_id);