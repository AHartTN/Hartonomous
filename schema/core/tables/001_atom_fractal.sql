-- ============================================================================
-- ATOM TABLE (FRACTAL RECURSIVE COMPOSITION) - REFERENCE DOCUMENTATION
-- ============================================================================
-- 
-- **THIS FILE IS REFERENCE DOCUMENTATION - NOT USED IN SCHEMA LOADING**
-- The actual schema is in 001_atom.sql (which has been updated with fractal composition)
--
-- This file preserved as documentation of the "Three Seashells" breakthrough
-- that solved the record explosion problem.
--
-- ============================================================================
-- THE BREAKTHROUGH: A Composition IS an Atom
-- ============================================================================
-- 
-- **THE SILVER BULLET SCHEMA CHANGE:**
--   composition_ids BIGINT[]  -- This IS the composition - no separate table!
--
-- This single change eliminates the "relational link farm" and enables:
-- 1. ONE ROW READ: "Hello World" = read one composition atom, get [ID_H, ID_e, ...]
-- 2. INSTANT INDEXING: GIN(composition_ids) answers "Which docs contain Legal Disclaimer?"
-- 3. O(1) DEDUPLICATION: Coordinate collision detects existing compositions
-- 4. FRACTAL COMPRESSION: "Lorem Ipsum" 1000x = ONE atom referenced 1000 times
--
-- Level 0: Primitives      '.'  -> atom_id=1
-- Level 1: Compositions    '...' -> atom_id=2, composition_ids=[1,1,1]
-- Level 2: Higher Order    'Hello...' -> atom_id=3, composition_ids=[atom_hello, 2]
-- 
-- This enables:
-- - Fractal deduplication: "Lorem Ipsum" stored once, referenced 1000 times
-- - O(1) lookup: coordinate = hash of composition
-- - Semantic compression: paragraphs, disclaimers, code blocks as single atoms
-- ============================================================================

CREATE TABLE IF NOT EXISTS atom (
    -- Identity
    atom_id BIGSERIAL PRIMARY KEY,
    
    -- Content addressing (global deduplication via SHA-256)
    content_hash BYTEA UNIQUE NOT NULL,
    
    -- PRIMITIVE VALUE (for Level 0 atoms)
    -- The "thing itself" - raw bytes, numbers, single characters
    atom_value BYTEA CHECK (length(atom_value) <= 64),
    
    -- COMPOSITE VALUE (for Level 1+ atoms)
    -- The "recipe" - array of child atom IDs
    -- If this is set, atom_value should be NULL (computed from children)
    -- This IS the composition - no separate table needed!
    composition_ids BIGINT[],
    
    -- Cached text representation (for display)
    canonical_text TEXT,
    
    -- Spatial semantics - 4D position in semantic space
    -- X, Y, Z: 3D semantic coordinates
    --   - Primitives: hash-based deterministic location
    --   - Compositions: f(child_coords) - midpoint, concept location, etc.
    -- M: Hilbert curve index OR sequence position
    spatial_key GEOMETRY(POINTZM, 0),
    
    -- Stability flag
    -- FALSE: Transient (streaming buffer, not yet crystallized)
    -- TRUE: Stable (permanent concept, reusable across documents)
    is_stable BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Importance / atomic mass (how often referenced)
    reference_count BIGINT NOT NULL DEFAULT 1,
    
    -- Flexible metadata
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
    -- Temporal versioning
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz,
    
    -- Constraints
    CONSTRAINT atom_value_xor_composition CHECK (
        -- Must have EITHER atom_value OR composition_ids, not both
        (atom_value IS NOT NULL AND composition_ids IS NULL) OR
        (atom_value IS NULL AND composition_ids IS NOT NULL)
    )
) USING columnar;

-- ============================================================================
-- INDEXES
-- ============================================================================

-- Content-addressable lookup (deduplication)
CREATE INDEX idx_atom_content_hash ON atom USING btree(content_hash);

-- Spatial indexes (dual strategy for NN search)
CREATE INDEX idx_atom_spatial_gist ON atom USING gist(spatial_key)
    WHERE spatial_key IS NOT NULL;

CREATE INDEX idx_atom_hilbert ON atom USING btree(
    ST_M(spatial_key)
) WHERE spatial_key IS NOT NULL;

-- Composition lookup (find atoms made from specific children)
CREATE INDEX idx_atom_composition_gin ON atom USING gin(composition_ids)
    WHERE composition_ids IS NOT NULL;

-- Stability (filter stable vs transient)
CREATE INDEX idx_atom_stable ON atom USING btree(is_stable);

-- Reference counting (find most important atoms)
CREATE INDEX idx_atom_reference_count ON atom USING btree(reference_count DESC);

-- Temporal versioning
CREATE INDEX idx_atom_temporal ON atom USING btree(valid_from, valid_to);

-- Metadata (modality, tenant, etc.)
CREATE INDEX idx_atom_metadata ON atom USING gin(metadata);

-- ============================================================================
-- COMMENTS
-- ============================================================================

COMMENT ON TABLE atom IS 'Fractal recursive composition table. EVERYTHING is an atom - primitives AND compositions. Enables O(1) deduplication via spatial coordinates.';

COMMENT ON COLUMN atom.atom_id IS 'Unique identifier for this atom';
COMMENT ON COLUMN atom.content_hash IS 'SHA-256 hash - for primitives: hash(atom_value), for compositions: hash(composition_ids)';
COMMENT ON COLUMN atom.atom_value IS 'Primitive value (≤64 bytes). NULL for compositions.';
COMMENT ON COLUMN atom.composition_ids IS 'Array of child atom IDs. NULL for primitives. This IS the composition!';
COMMENT ON COLUMN atom.canonical_text IS 'Cached text representation for display (computed from children if composition)';
COMMENT ON COLUMN atom.spatial_key IS '4D semantic coordinate. Primitives: hash-based. Compositions: f(child_coords).';
COMMENT ON COLUMN atom.is_stable IS 'FALSE: transient (streaming buffer). TRUE: stable (permanent, reusable concept).';
COMMENT ON COLUMN atom.reference_count IS 'How many times this atom is referenced (atomic mass)';
COMMENT ON COLUMN atom.metadata IS 'Flexible JSONB: modality, tenant, model, concept_type, etc.';

-- ============================================================================
-- EXAMPLES
-- ============================================================================

-- Example: "..." (ellipsis) as a composition
-- 
-- Step 1: Ensure '.' exists
-- INSERT INTO atom (content_hash, atom_value, spatial_key, is_stable)
-- VALUES (sha256('.'), '.', compute_position('.'), TRUE)
-- RETURNING atom_id; -- Returns 1
-- 
-- Step 2: Create '...' as composition
-- INSERT INTO atom (content_hash, composition_ids, spatial_key, canonical_text, is_stable)
-- VALUES (
--     sha256(array[1,1,1]::BIGINT[]),  -- Hash of composition
--     ARRAY[1,1,1],                      -- Three periods
--     compute_composition_position(ARRAY[1,1,1]),  -- Midpoint or concept location
--     '...',                             -- Cached text
--     TRUE                               -- Stable concept
-- );
-- 
-- Step 3: Use '...' 1000 times
-- Your document just stores atom_id=2 repeated, NOT [1,1,1] repeated 1000 times
-- 
-- Result: 
-- - "Lorem Ipsum..." (1000 words) = array of ~50 paragraph atoms
-- - NOT array of 5000 character atoms
-- - 100x compression for repetitive content
