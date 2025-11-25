-- ============================================================================
-- Find Similar Audio Waveforms
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Query by waveform geometry similarity
-- ============================================================================

CREATE OR REPLACE FUNCTION find_similar_audio(
    p_waveform GEOMETRY,
    p_limit INTEGER DEFAULT 10
)
RETURNS TABLE(
    atom_id BIGINT,
    canonical_text TEXT,
    similarity REAL,
    waveform GEOMETRY
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT 
        a.atom_id,
        a.canonical_text,
        (1.0 / (1.0 + ST_HausdorffDistance(a.spatial_key, p_waveform)))::REAL AS similarity,
        a.spatial_key AS waveform
    FROM atom a
    WHERE a.metadata->>'modality' = 'audio'
      AND a.spatial_key IS NOT NULL
    ORDER BY ST_HausdorffDistance(a.spatial_key, p_waveform) ASC
    LIMIT p_limit;
END;
$$;

COMMENT ON FUNCTION find_similar_audio(GEOMETRY, INTEGER) IS 
'Find similar audio waveforms using Hausdorff distance on LINESTRING geometry.';
