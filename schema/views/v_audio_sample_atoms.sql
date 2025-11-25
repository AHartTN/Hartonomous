-- ============================================================================
-- Audio Sample Atoms View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Individual audio samples as atoms
-- ============================================================================

CREATE VIEW v_audio_sample_atoms AS
SELECT 
    a.atom_id,
    (a.metadata->>'time')::REAL AS time,
    (a.metadata->>'amplitude')::REAL AS amplitude,
    (a.metadata->>'channel')::INTEGER AS channel,
    (a.metadata->>'sample_rate')::INTEGER AS sample_rate,
    (a.metadata->>'sample_index')::INTEGER AS sample_index,
    a.spatial_key AS position,
    a.reference_count,
    a.created_at,
    a.metadata
FROM atom a
WHERE a.metadata->>'modality' = 'audio_sample';

CREATE INDEX ON v_audio_sample_atoms (time);
CREATE INDEX ON v_audio_sample_atoms (channel);

COMMENT ON VIEW v_audio_sample_atoms IS 
'Individual audio samples as atoms with time and amplitude.';
