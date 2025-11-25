-- ============================================================================
-- Reconstruct Sparse Audio (Fill Silence Gaps)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Rebuild waveform from sparse samples, interpolate silence
-- ============================================================================

CREATE OR REPLACE FUNCTION reconstruct_audio_sparse(
    p_sample_atom_ids BIGINT[],
    p_sample_rate INTEGER
)
RETURNS TABLE(sample_index INTEGER, time REAL, amplitude REAL)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH stored_samples AS (
        SELECT 
            (a.metadata->>'sample_index')::INTEGER AS idx,
            (a.metadata->>'time')::REAL AS t,
            (a.metadata->>'amplitude')::REAL AS amp
        FROM atom a
        WHERE a.atom_id = ANY(p_sample_atom_ids)
        ORDER BY idx
    ),
    sample_range AS (
        SELECT 
            MIN(idx) AS min_idx,
            MAX(idx) AS max_idx
        FROM stored_samples
    ),
    full_sequence AS (
        SELECT generate_series(min_idx, max_idx) AS idx
        FROM sample_range
    )
    SELECT 
        f.idx,
        f.idx::REAL / p_sample_rate AS time,
        COALESCE(s.amp, 0.0) AS amplitude  -- Fill gaps with silence
    FROM full_sequence f
    LEFT JOIN stored_samples s ON s.idx = f.idx
    ORDER BY f.idx;
END;
$$;

COMMENT ON FUNCTION reconstruct_audio_sparse(BIGINT[], INTEGER) IS 
'Reconstruct audio from sparse samples. Gaps (missing atom_ids) filled with silence (0.0).';
