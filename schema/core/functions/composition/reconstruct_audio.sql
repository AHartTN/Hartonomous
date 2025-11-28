-- ============================================================================
-- Reconstruct Audio from Sample Atoms
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Inverse operation: rebuild waveform from atomized samples
-- ============================================================================

CREATE OR REPLACE FUNCTION reconstruct_audio(p_sample_atom_ids BIGINT[])
RETURNS TABLE(time_val REAL, amplitude REAL, channel INTEGER)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT 
        (a.metadata->>'time')::REAL,
        (a.metadata->>'amplitude')::REAL,
        (a.metadata->>'channel')::INTEGER
    FROM atom a
    WHERE a.atom_id = ANY(p_sample_atom_ids)
    ORDER BY 
        (a.metadata->>'channel')::INTEGER,
        (a.metadata->>'time')::REAL;
END;
$$;

COMMENT ON FUNCTION reconstruct_audio(BIGINT[]) IS 
'Reconstruct audio waveform from sample atom IDs. Returns ordered (time, amplitude, channel) tuples.';
