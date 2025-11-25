-- ============================================================================
-- Compute Self-Attention (Query-Key-Value)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Transformer-style self-attention via spatial KNN
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_attention(
    p_query_atom_id BIGINT,
    p_context_atom_ids BIGINT[],
    p_k INTEGER DEFAULT 10  -- Top-K attention
)
RETURNS TABLE(
    atom_id BIGINT,
    attention_weight REAL,
    canonical_text TEXT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_query_position GEOMETRY;
    v_total_similarity REAL;
BEGIN
    -- Get query atom position
    SELECT spatial_key INTO v_query_position
    FROM atom
    WHERE atom_id = p_query_atom_id;
    
    IF v_query_position IS NULL THEN
        RAISE EXCEPTION 'Query atom % has no spatial position', p_query_atom_id;
    END IF;
    
    -- Compute softmax attention weights via spatial similarity
    RETURN QUERY
    WITH similarities AS (
        SELECT 
            a.atom_id,
            a.canonical_text,
            -- Similarity = inverse distance (attention score)
            1.0 / (1.0 + ST_Distance(v_query_position, a.spatial_key)) AS similarity
        FROM atom a
        WHERE a.atom_id = ANY(p_context_atom_ids)
          AND a.spatial_key IS NOT NULL
        ORDER BY ST_Distance(v_query_position, a.spatial_key)
        LIMIT p_k
    ),
    softmax AS (
        SELECT 
            atom_id,
            canonical_text,
            similarity,
            SUM(similarity) OVER () AS total_similarity
        FROM similarities
    )
    SELECT 
        atom_id,
        (similarity / total_similarity)::REAL AS attention_weight,
        canonical_text
    FROM softmax
    ORDER BY attention_weight DESC;
END;
$$;

COMMENT ON FUNCTION compute_attention(BIGINT, BIGINT[], INTEGER) IS 
'Transformer-style self-attention via spatial KNN.
Query atom attends to K nearest context atoms. Weights = softmax(similarity).
Use for: next-token prediction, context selection, semantic focus.';
