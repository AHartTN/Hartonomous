-- novel_schema.sql
-- A Spatial Semantic Unicode Database Schema
-- Designed for High-Dimensional deterministic atom placement.

-- 1. THE ATOM (The Immutable Core)
CREATE TABLE IF NOT EXISTS atoms (
    id             INTEGER PRIMARY KEY,  -- The Codepoint Value (0 - 1114111) e.g. 65 for 'A'
    hex            TEXT GENERATED ALWAYS AS (upper(to_hex(id))) STORED, -- For display: '41'
    scalar         TEXT,                 -- The actual character (if printable/valid), else NULL
    name           TEXT,                 -- The 'na' attribute (Name)
    
    -- Physical Properties (Hot Columns for filtering)
    block          TEXT,                 -- The 'blk' attribute
    general_category TEXT,               -- The 'gc' attribute
    age            TEXT,                 -- The 'age' attribute
    
    -- The "Everything" Bucket
    -- All other UCD properties stored efficiently as binary JSON.
    metadata       JSONB
);

-- Indexes for Atomic Lookups
CREATE INDEX IF NOT EXISTS idx_atoms_block ON atoms(block);
CREATE INDEX IF NOT EXISTS idx_atoms_gc ON atoms(general_category);
CREATE INDEX IF NOT EXISTS idx_atoms_metadata ON atoms USING GIN (metadata); -- Allows querying inside JSONB

-- 2. THE SPACETIME (The Mutable Context)
CREATE TABLE IF NOT EXISTS atom_coordinates (
    atom_id        INTEGER REFERENCES atoms(id) ON DELETE CASCADE,
    
    -- 4D Coordinate System (x, y, z, w)
    pos_x          DOUBLE PRECISION DEFAULT 0.0,
    pos_y          DOUBLE PRECISION DEFAULT 0.0,
    pos_z          DOUBLE PRECISION DEFAULT 0.0,
    pos_w          DOUBLE PRECISION DEFAULT 0.0,
    
    -- Manifold / Perimeter assignment
    manifold_id    INTEGER DEFAULT 0,
    sequence_index INTEGER DEFAULT 0,
    
    PRIMARY KEY (atom_id)
);

-- Spatial Indexes
CREATE INDEX IF NOT EXISTS idx_atom_coords_x ON atom_coordinates(pos_x);

-- 3. THE MOLECULES (The Compositions)
-- Trajectories or Linestrings of Atoms.
-- Supports Named Sequences, Standardized Variants, Emoji Sequences.
CREATE TABLE IF NOT EXISTS molecules (
    id             BIGSERIAL PRIMARY KEY,
    
    -- The definition: An ordered array of Atom IDs.
    -- e.g., [67, 97, 116] for "Cat"
    atom_ids       INTEGER[] NOT NULL,
    
    -- Classification
    type           TEXT, -- 'NamedSequence', 'StandardizedVariant', 'EmojiSequence', 'UserDefined'
    
    -- Semantic Tag / Label
    label          TEXT, -- e.g. "LATIN SMALL LETTER A WITH ...", "emoji-zwj-sequence"
    
    -- Additional Metadata
    metadata       JSONB,
    
    -- Geometric Cache
    centroid_x     DOUBLE PRECISION,
    centroid_y     DOUBLE PRECISION,
    centroid_z     DOUBLE PRECISION,
    centroid_w     DOUBLE PRECISION
);

CREATE INDEX IF NOT EXISTS idx_molecules_type ON molecules(type);
CREATE INDEX IF NOT EXISTS idx_molecules_atom_ids ON molecules USING GIN (atom_ids);

-- 4. UNIHAN PROPERTIES (Rich structured data for CJK)
-- Separated for performance and clarity.
CREATE TABLE IF NOT EXISTS unihan_properties (
    atom_id        INTEGER PRIMARY KEY REFERENCES atoms(id) ON DELETE CASCADE,
    
    -- Radicals & Strokes
    k_rs_unicode       TEXT, -- Radical/Stroke index
    k_total_strokes    TEXT, -- Total stroke count (text because it can be multiple values)
    
    -- Readings & Pronunciations
    k_mandarin         TEXT,
    k_cantonese        TEXT,
    k_japanese         TEXT,
    k_korean           TEXT,
    k_vietnamese       TEXT,
    k_hanyu_pinyin     TEXT,
    
    -- Definitions & Sources
    k_definition       TEXT,
    k_cangjie          TEXT,
    
    -- Dictionary Indices
    k_kangxi           TEXT,
    k_han_yu           TEXT,
    k_ir_g_hanyu_da_zidian TEXT,
    
    -- Variants
    k_semantic_variant TEXT,
    k_simplified_variant TEXT,
    k_traditional_variant TEXT,
    k_z_variant        TEXT
);

-- 5. SEMANTIC RANGES (Structured data from .txt files)
-- Replaces raw file dumps with proper relational data.

CREATE TABLE IF NOT EXISTS unicode_blocks (
    id             SERIAL PRIMARY KEY,
    name           TEXT NOT NULL,
    start_cp       INTEGER NOT NULL,
    end_cp         INTEGER NOT NULL,
    
    UNIQUE(name)
);
CREATE INDEX IF NOT EXISTS idx_unicode_blocks_range ON unicode_blocks(start_cp, end_cp);

CREATE TABLE IF NOT EXISTS unicode_scripts (
    id             SERIAL PRIMARY KEY,
    name           TEXT NOT NULL,
    short_name     TEXT, 
    start_cp       INTEGER NOT NULL,
    end_cp         INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_unicode_scripts_range ON unicode_scripts(start_cp, end_cp);

CREATE TABLE IF NOT EXISTS unicode_line_breaks (
    id             SERIAL PRIMARY KEY,
    property       TEXT NOT NULL, -- e.g. "BK", "CR"
    start_cp       INTEGER NOT NULL,
    end_cp         INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_unicode_line_breaks_range ON unicode_line_breaks(start_cp, end_cp);

-- 6. GENERIC ATOM PROPERTIES (EAV)
-- Captures "Code; Value" files like ArabicShaping, BidiMirroring, etc.
-- More structured than raw_file_data, allowing query by property name.
CREATE TABLE IF NOT EXISTS atom_properties (
    atom_id        INTEGER NOT NULL REFERENCES atoms(id) ON DELETE CASCADE,
    property       TEXT NOT NULL, -- e.g., "Joining_Type", "Bidi_Mirroring_Glyph"
    value          TEXT NOT NULL,
    filename       TEXT,          -- Provenance
    
    PRIMARY KEY (atom_id, property)
);
CREATE INDEX IF NOT EXISTS idx_atom_properties_prop ON atom_properties(property);

-- 7. COMPLIANCE TESTS
-- Stores test vectors from *Test.txt files.
CREATE TABLE IF NOT EXISTS compliance_tests (
    id             BIGSERIAL PRIMARY KEY,
    filename       TEXT NOT NULL,
    line_number    INTEGER,
    
    -- Inputs (often sequences of hex)
    input_sequence INTEGER[],
    
    -- Outputs (can be complex, storing as text or JSON for flexibility)
    -- e.g. Normalization: c1, c2, c3...
    -- e.g. Bidi: Levels, Order
    expected_output TEXT, 
    
    metadata       JSONB -- Comments, extra flags
);

-- 8. RAW FILE STORAGE (The Safety Net)
-- Stores line-by-line content of every text file to ensure nothing is lost.
-- We can refine/parse these into structured tables later.
CREATE TABLE IF NOT EXISTS raw_file_data (
    id             BIGSERIAL PRIMARY KEY,
    filename       TEXT NOT NULL,
    line_number    INTEGER NOT NULL,
    content        TEXT,
    
    -- Extracted range (if detected)
    start_cp       INTEGER, 
    end_cp         INTEGER,
    
    -- Metadata tags (if detected)
    property       TEXT,
    value          TEXT,
    
    UNIQUE(filename, line_number)
);

CREATE INDEX IF NOT EXISTS idx_raw_file_data_filename ON raw_file_data(filename);
CREATE INDEX IF NOT EXISTS idx_raw_file_data_range ON raw_file_data(start_cp, end_cp);