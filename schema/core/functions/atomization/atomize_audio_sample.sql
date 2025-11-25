-- ============================================================================
-- Atomize Audio Sample
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Single audio sample (amplitude at time t)
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_audio_sample(
    p_amplitude REAL,
    p_time REAL,
    p_channel INTEGER DEFAULT 0,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_sample_bytes BYTEA;
    v_spatial_key GEOMETRY;
BEGIN
    -- Pack amplitude as float4 (4 bytes)
    v_sample_bytes := (p_amplitude::REAL)::TEXT::BYTEA;
    
    -- Store as 1D position (time) with amplitude as Z
    v_spatial_key := ST_MakePoint(p_time, 0, p_amplitude);
    
    -- Atomize with spatial position (helper)
    RETURN atomize_with_spatial_key(
        v_sample_bytes,
        v_spatial_key,
        p_amplitude::TEXT,
        p_metadata || jsonb_build_object(
            'modality', 'audio_sample',
            'time', p_time,
            'amplitude', p_amplitude,
            'channel', p_channel
        )
    );
END;
$$;

COMMENT ON FUNCTION atomize_audio_sample(REAL, REAL, INTEGER, JSONB) IS 
'Atomize single audio sample: amplitude at time t.';
