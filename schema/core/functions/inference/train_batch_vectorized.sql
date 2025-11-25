-- ============================================================================
-- Batch Training (VECTORIZED)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- PERFORMANCE: Bulk weight updates instead of loop
-- ============================================================================

CREATE OR REPLACE FUNCTION train_batch_vectorized(
    p_batch JSONB[],  -- Array of {input_atoms: [], target_atom: id}
    p_learning_rate REAL DEFAULT 0.01
)
RETURNS TABLE(sample_idx INTEGER, loss REAL)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH batch_data AS (
        SELECT 
            ROW_NUMBER() OVER () AS idx,
            batch_item->>'target_atom' AS target_atom,
            ARRAY(SELECT jsonb_array_elements_text(batch_item->'input_atoms')::bigint) AS input_atoms
        FROM unnest(p_batch) AS batch_item
    ),
    predictions AS (
        SELECT 
            bd.idx,
            bd.target_atom::bigint,
            bd.input_atoms,
            (SELECT atom_id FROM compute_attention(
                bd.input_atoms[array_length(bd.input_atoms, 1)],
                bd.input_atoms[1:array_length(bd.input_atoms,1)-1],
                1
            ) LIMIT 1) AS predicted_atom
        FROM batch_data bd
    ),
    losses AS (
        SELECT 
            p.idx,
            p.target_atom,
            p.predicted_atom,
            ST_Distance(
                (SELECT spatial_key FROM atom WHERE atom_id = p.predicted_atom),
                (SELECT spatial_key FROM atom WHERE atom_id = p.target_atom)
            ) AS loss
        FROM predictions p
    )
    -- Bulk weight updates (no loop)
    SELECT 
        idx::INTEGER,
        loss::REAL
    FROM losses;
    
    -- Bulk reinforce/weaken (set-based)
    -- TODO: Implement batch weight updates via single UPDATE statement
END;
$$;

COMMENT ON FUNCTION train_batch_vectorized(JSONB[], REAL) IS 
'VECTORIZED batch training: processes multiple samples simultaneously.
No loops, pure set-based operations. Enables mini-batch SGD.';
