-- UCD Enterprise Normalized Schema
-- Enforces strict referential integrity, 3NF, and type safety.
-- No hardcoded data. All lookups must be ingested.

DROP TABLE IF EXISTS unicode_aliases CASCADE;
DROP TABLE IF EXISTS binary_properties CASCADE;
DROP TABLE IF EXISTS numeric_properties CASCADE;
DROP TABLE IF EXISTS string_properties CASCADE;
DROP TABLE IF EXISTS unihan_properties CASCADE;
DROP TABLE IF EXISTS code_points CASCADE;
DROP TABLE IF EXISTS scripts CASCADE;
DROP TABLE IF EXISTS blocks CASCADE;
DROP TABLE IF EXISTS bidi_classes CASCADE;
DROP TABLE IF EXISTS general_categories CASCADE;
DROP TABLE IF EXISTS ucd_versions CASCADE;
DROP TABLE IF EXISTS derived_ages CASCADE;
DROP TABLE IF EXISTS sequences CASCADE;
DROP TABLE IF EXISTS test_data CASCADE;

-- 1. Metadata
CREATE TABLE ucd_versions (
    id SERIAL PRIMARY KEY,
    version_string VARCHAR(20) NOT NULL UNIQUE,
    ingest_timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 2. Lookup Tables (Enums/Dependencies)
CREATE TABLE general_categories (
    id SERIAL PRIMARY KEY,
    abbreviation VARCHAR(10) NOT NULL UNIQUE,
    description VARCHAR(255) NOT NULL
);

CREATE TABLE bidi_classes (
    id SERIAL PRIMARY KEY,
    abbreviation VARCHAR(10) NOT NULL UNIQUE,
    description VARCHAR(255) NOT NULL
);

CREATE TABLE scripts (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    iso_code VARCHAR(20) -- Increased size just in case
);

CREATE TABLE blocks (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL UNIQUE,
    start_cp INTEGER NOT NULL,
    end_cp INTEGER NOT NULL,
    CHECK (start_cp <= end_cp)
);
CREATE INDEX idx_blocks_range ON blocks(start_cp, end_cp);

CREATE TABLE derived_ages (
    id SERIAL PRIMARY KEY,
    version VARCHAR(20) NOT NULL UNIQUE
);

-- 3. Main Code Point Table (The "Atom")
CREATE TABLE code_points (
    codepoint INTEGER PRIMARY KEY, -- 0x0000 to 0x10FFFF
    name VARCHAR(255) NOT NULL,
    
    -- Foreign Keys
    general_category_id INTEGER REFERENCES general_categories(id),
    bidi_class_id INTEGER REFERENCES bidi_classes(id),
    block_id INTEGER REFERENCES blocks(id),
    script_id INTEGER REFERENCES scripts(id),
    age_id INTEGER REFERENCES derived_ages(id),

    -- Intrinsic numeric values
    numeric_value_decimal INTEGER,
    numeric_value_digit INTEGER,
    numeric_value_numeric DOUBLE PRECISION,
    
    -- Simple Case Mappings
    simple_uppercase_mapping INTEGER,
    simple_lowercase_mapping INTEGER,
    simple_titlecase_mapping INTEGER
);

-- 4. Property Tables (Separated by Type)

CREATE TABLE binary_properties (
    codepoint INTEGER NOT NULL REFERENCES code_points(codepoint) ON DELETE CASCADE,
    property VARCHAR(100) NOT NULL,
    value BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (codepoint, property)
);

CREATE TABLE numeric_properties (
    codepoint INTEGER NOT NULL REFERENCES code_points(codepoint) ON DELETE CASCADE,
    property VARCHAR(100) NOT NULL,
    value INTEGER NOT NULL,
    PRIMARY KEY (codepoint, property)
);

CREATE TABLE string_properties (
    codepoint INTEGER NOT NULL REFERENCES code_points(codepoint) ON DELETE CASCADE,
    property VARCHAR(100) NOT NULL,
    value TEXT NOT NULL,
    PRIMARY KEY (codepoint, property)
);

CREATE TABLE unihan_properties (
    codepoint INTEGER NOT NULL REFERENCES code_points(codepoint) ON DELETE CASCADE,
    tag VARCHAR(255) NOT NULL, -- Increased from 100
    value TEXT NOT NULL,
    PRIMARY KEY (codepoint, tag)
);

-- 5. Aliases
CREATE TABLE unicode_aliases (
    id SERIAL PRIMARY KEY,
    codepoint INTEGER NOT NULL REFERENCES code_points(codepoint) ON DELETE CASCADE,
    alias VARCHAR(255) NOT NULL,
    type VARCHAR(50) NOT NULL,
    UNIQUE (codepoint, alias, type) -- IDEMPOTENCY ENFORCED
);

-- 6. Sequences
CREATE TABLE sequences (
    id SERIAL PRIMARY KEY,
    sequence INTEGER[] NOT NULL,
    type VARCHAR(100) NOT NULL,
    description TEXT,
    metadata JSONB,
    UNIQUE (sequence, type) -- IDEMPOTENCY ENFORCED
);

-- 7. Test Data
CREATE TABLE test_data (
    id SERIAL PRIMARY KEY,
    test_suite VARCHAR(100) NOT NULL,
    line_number INTEGER,
    input_codepoints INTEGER[] NOT NULL,
    expected_output JSONB,
    comment TEXT
);