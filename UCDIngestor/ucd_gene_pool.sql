-- =========================================================================================
-- HARTONOMOUS GENE POOL SCHEMA (UCD)
-- =========================================================================================
-- This schema serves as the authoritative "Staging Area" for Unicode Metadata.
-- It is populated from the official UCD XML and Text files.
-- It is READ-ONLY for the Hartonomous Engine (which uses it to seed Atoms).
-- =========================================================================================

CREATE SCHEMA IF NOT EXISTS ucd;

-- 1. Core Code Points (The Encyclopedia)
-- Sourced from: ucd.all.flat.xml
-- "Identity" is the Codepoint Integer.
DROP TABLE IF EXISTS ucd.code_points CASCADE;
CREATE TABLE ucd.code_points (
    codepoint INTEGER PRIMARY KEY,          -- The integer value (0..1114111)
    hex_str CHAR(6) NOT NULL,               -- Hex string (e.g., '000041')
    
    -- Core Identity Properties (Hot Columns)
    name TEXT,                              -- 'na'
    general_category CHAR(2),               -- 'gc' (Lu, Ll, etc.)
    canonical_combining_class INTEGER,      -- 'ccc'
    bidi_class TEXT,                        -- 'bc'
    decomposition_type TEXT,                -- 'dt'
    decomposition_mapping TEXT,             -- 'dm' (Hex sequence)
    
    numeric_value_dec TEXT,                 -- 'nv' (Can be fraction like '-1/2')
    numeric_type TEXT,                      -- 'nt'
    
    age TEXT,                               -- 'age' (e.g., '1.1', '15.0')
    block TEXT,                             -- 'blk'
    script TEXT,                            -- 'sc'
    
    -- Extended Property Bag
    -- All other attributes from XML are stored here to handle schema evolution
    -- without altering table structure.
    properties JSONB
);

-- Indexes for frequent lookups during Atom Seeding
CREATE INDEX idx_ucd_cp_gc ON ucd.code_points(general_category);
CREATE INDEX idx_ucd_cp_script ON ucd.code_points(script);
CREATE INDEX idx_ucd_cp_block ON ucd.code_points(block);


-- 2. Collation Weights (The Sequencing Logic)
-- Sourced from: allkeys.txt (DUCET)
-- Defines the semantic ordering weights.
DROP TABLE IF EXISTS ucd.collation_weights CASCADE;
CREATE TABLE ucd.collation_weights (
    id SERIAL PRIMARY KEY,
    source_codepoints INTEGER[] NOT NULL,   -- Array of CPs (handling contractions like 'ch')
    
    -- The Weight Tuple
    primary_weight INTEGER NOT NULL,
    secondary_weight INTEGER NOT NULL,
    tertiary_weight INTEGER NOT NULL,
    
    is_variable BOOLEAN DEFAULT FALSE       -- '*' in DUCET
);

-- Index for searching weights by codepoint (using array containment)
CREATE INDEX idx_ucd_col_source ON ucd.collation_weights USING GIN(source_codepoints);
CREATE INDEX idx_ucd_col_weights ON ucd.collation_weights(primary_weight, secondary_weight, tertiary_weight);


-- 3. Confusables (The Security Graph)
-- Sourced from: confusables.txt
-- Defines "visual adjacency" edges.
DROP TABLE IF EXISTS ucd.confusables CASCADE;
CREATE TABLE ucd.confusables (
    source_codepoint INTEGER NOT NULL,
    target_codepoints INTEGER[] NOT NULL,   -- Can map to sequence
    
    confusable_type TEXT                    -- 'MA', 'L', 'SA' (Mixed-Script, Latin, etc.)
);

CREATE INDEX idx_ucd_conf_source ON ucd.confusables(source_codepoint);


-- 4. Emoji Sequences (The Composite Atoms)
-- Sourced from: emoji-sequences.txt, emoji-zwj-sequences.txt
DROP TABLE IF EXISTS ucd.emoji_sequences CASCADE;
CREATE TABLE ucd.emoji_sequences (
    sequence_codepoints INTEGER[] NOT NULL, -- The sequence
    type_field TEXT,                        -- 'RGI_Emoji_Zwj_Sequence', etc.
    description TEXT,
    
    PRIMARY KEY (sequence_codepoints)
);
