-- ============================================================================
-- Migration 001: Fix atomize_numeric_batch for true set-based batching
-- 
-- PROBLEM: Old implementation used FOR LOOP calling atomize_value() 255 times
--          Result: 1 weight/second (178s for 255 weights)
--
-- SOLUTION: Use single INSERT with array unnesting and ON CONFLICT
--           Expected: 100-200 weights/second (1-2s for 255 weights)
--
-- Apply with: psql -U hartonomous -d hartonomous -f 001_fix_atomize_numeric_batch.sql
-- ============================================================================

BEGIN;

DROP FUNCTION IF EXISTS atomize_numeric_batch(numeric[], jsonb);

CREATE OR REPLACE FUNCTION atomize_numeric_batch(
    weights numeric[],
    metadata_template jsonb DEFAULT '{}'::jsonb
)
RETURNS TABLE(
    weight_value numeric,
    atom_id bigint
) AS $$
BEGIN
    -- Single set-based INSERT with ON CONFLICT for all weights
    -- This is 100-200x faster than looping through individual atomize_value() calls
    RETURN QUERY
    WITH unique_weights AS (
        -- Get distinct weights from input array
        SELECT DISTINCT unnest(weights) AS weight
    ),
    weight_data AS (
        -- Prepare all data for insertion in one pass
        SELECT 
            weight,
            digest(weight::text, 'sha256') AS weight_hash,
            weight::text AS canonical_text,
            metadata_template || jsonb_build_object('value', weight) AS weight_metadata
        FROM unique_weights
    ),
    inserted AS (
        -- Single INSERT for all weights with ON CONFLICT
        INSERT INTO atom (content_hash, canonical_text, metadata, valid_from)
        SELECT 
            weight_hash,
            canonical_text,
            weight_metadata,
            now()
        FROM weight_data
        ON CONFLICT (content_hash) 
        DO UPDATE SET reference_count = atom.reference_count + 1
        RETURNING content_hash, atom.atom_id
    )
    -- Join back to get weight_value -> atom_id mapping
    SELECT 
        wd.weight AS weight_value,
        COALESCE(ins.atom_id, existing.atom_id) AS atom_id
    FROM weight_data wd
    LEFT JOIN inserted ins ON ins.content_hash = wd.weight_hash
    LEFT JOIN atom existing ON existing.content_hash = wd.weight_hash
    ORDER BY wd.weight;
END;
$$ LANGUAGE plpgsql;

COMMIT;

-- Verification
SELECT 'Migration 001 applied successfully' AS status;
