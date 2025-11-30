-- ============================================================================
-- Core Atomization Function
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Content-addressable storage with SHA-256 deduplication
-- 
-- This is the fundamental atomization interface that all other atomization
-- functions build upon. It enforces:
--   1. Content addressing (SHA-256 hash)
--   2. Global deduplication
--   3. 64-byte size constraint
--   4. Reference counting
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_value(
    p_value BYTEA,
    p_canonical_text TEXT DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_hash BYTEA;
    v_atom_id BIGINT;
BEGIN
    -- Validate size constraint (?64 bytes)
    IF length(p_value) > 64 THEN
        RAISE EXCEPTION 
            'Atomic value exceeds 64-byte limit (got % bytes). Values larger than 64 bytes must be decomposed into smaller atoms.',
            length(p_value)
            USING HINT = 'Use atomize_text() for strings or decompose manually';
    END IF;
    
    -- Compute SHA-256 hash for content addressing
    v_hash := digest(p_value, 'sha256');
    
    -- Attempt to find existing atom (deduplication)
    SELECT atom_id INTO v_atom_id
    FROM atom 
    WHERE content_hash = v_hash;
    
    IF FOUND THEN
        -- Atom exists: increment reference count (conservation of reference)
        UPDATE atom 
        SET reference_count = reference_count + 1,
            -- Update metadata if new information provided
            metadata = CASE 
                WHEN p_metadata != '{}'::jsonb 
                THEN atom.metadata || p_metadata 
                ELSE atom.metadata 
            END
        WHERE atom_id = v_atom_id;
        
        RETURN v_atom_id;
    END IF;
    
    -- Atom doesn't exist: create new atom
    INSERT INTO atom (
        content_hash, 
        atom_value, 
        canonical_text, 
        metadata,
        reference_count
    )
    VALUES (
        v_hash, 
        p_value, 
        p_canonical_text, 
        p_metadata,
        1  -- Initial reference count
    )
    RETURNING atom_id INTO v_atom_id;
    
    RETURN v_atom_id;
    
EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'Atomization failed: %', SQLERRM
            USING HINT = 'Check input value and metadata format';
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION atomize_value(BYTEA, TEXT, JSONB) IS 
'Core atomization interface: SHA-256 content addressing with automatic deduplication. 
Returns atom_id of created or existing atom. Enforces ?64 byte constraint.

Parameters:
  p_value - Binary value to atomize (?64 bytes required)
  p_canonical_text - Optional text representation for caching
  p_metadata - Optional JSONB metadata (modality, model_name, etc.)

Returns:
  BIGINT - atom_id of the atomized value

Example:
  SELECT atomize_value(''\x48''::bytea, ''H'', ''{"modality": "character"}''::jsonb);';
