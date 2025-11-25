-- ============================================================================
-- Batch Spatial Position Update (VECTORIZED)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- PERFORMANCE: Single UPDATE with subquery instead of loop
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_spatial_positions_vectorized(
    p_atom_ids BIGINT[] DEFAULT NULL,  -- NULL = all atoms without position
    p_batch_size INTEGER DEFAULT 10000
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_count BIGINT := 0;
    v_batch_count BIGINT;
BEGIN
    LOOP
        -- Batch update (no loop over individual atoms)
        WITH batch AS (
            SELECT 
                a.atom_id,
                compute_spatial_position(a.atom_id) AS new_position
            FROM atom a
            WHERE (p_atom_ids IS NULL AND a.spatial_key IS NULL)
               OR (p_atom_ids IS NOT NULL AND a.atom_id = ANY(p_atom_ids))
            LIMIT p_batch_size
        )
        UPDATE atom
        SET spatial_key = batch.new_position
        FROM batch
        WHERE atom.atom_id = batch.atom_id;
        
        GET DIAGNOSTICS v_batch_count = ROW_COUNT;
        v_count := v_count + v_batch_count;
        
        EXIT WHEN v_batch_count = 0;
    END LOOP;
    
    RETURN v_count;
END;
$$;

COMMENT ON FUNCTION compute_spatial_positions_vectorized(BIGINT[], INTEGER) IS 
'VECTORIZED spatial position computation: batch UPDATE instead of loop.
Processes 10K atoms per batch. PostgreSQL parallelizes automatically.';
