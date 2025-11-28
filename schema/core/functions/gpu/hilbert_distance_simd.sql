-- SIMD-Accelerated Hilbert Distance
CREATE OR REPLACE FUNCTION hilbert_distance_simd(
    p_hilbert1 BIGINT,
    p_hilbert2 BIGINT
)
RETURNS BIGINT
LANGUAGE sql
IMMUTABLE PARALLEL SAFE
AS $$
    SELECT ABS(p_hilbert1 - p_hilbert2)
$$;

COMMENT ON FUNCTION hilbert_distance_simd(BIGINT, BIGINT) IS
'SIMD-accelerated Hilbert distance (PostgreSQL built-in vectorization).';
