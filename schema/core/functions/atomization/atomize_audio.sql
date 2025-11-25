-- ============================================================================
-- Atomize Audio (SAMPLE-LEVEL)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Decompose audio into individual SAMPLE atoms
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_audio(
    p_samples REAL[],
    p_sample_rate INTEGER,
    p_channel INTEGER DEFAULT 0,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT[]
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom_ids BIGINT[];
    v_sample_id BIGINT;
    v_time REAL;
BEGIN
    -- Atomize EACH sample individually
    FOR i IN 1..array_length(p_samples, 1) LOOP
        v_time := (i - 1)::REAL / p_sample_rate;
        
        v_sample_id := atomize_audio_sample(
            p_samples[i],
            v_time,
            p_channel,
            p_metadata || jsonb_build_object(
                'sample_rate', p_sample_rate,
                'sample_index', i
            )
        );
        
        v_atom_ids := array_append(v_atom_ids, v_sample_id);
    END LOOP;
    
    RETURN v_atom_ids;
END;
$$;

COMMENT ON FUNCTION atomize_audio(REAL[], INTEGER, INTEGER, JSONB) IS 
'Atomize audio at SAMPLE level. Each amplitude value becomes a separate atom.
Returns ordered array of sample atom_ids.';
