-- Hybrid Attention (CPU or GPU)
CREATE OR REPLACE FUNCTION compute_attention_hybrid(
    p_query_atom_id BIGINT,
    p_context_atom_ids BIGINT[],
    p_k INTEGER DEFAULT 10
)
RETURNS TABLE(
    atom_id BIGINT,
    attention_weight REAL,
    canonical_text TEXT
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF gpu_available() THEN
        RETURN QUERY SELECT * FROM compute_attention_gpu(p_query_atom_id, p_context_atom_ids, p_k);
    ELSE
        RETURN QUERY SELECT * FROM compute_attention(p_query_atom_id, p_context_atom_ids, p_k);
    END IF;
END;
$$;

COMMENT ON FUNCTION compute_attention_hybrid(BIGINT, BIGINT[], INTEGER) IS
'Hybrid attention: uses GPU if available, falls back to CPU PostGIS version.';
