-- ============================================================================
-- Sparse Audio Storage (Skip Silence)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Only store non-zero samples, gaps = implicit silence
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_audio_sparse(
    p_samples REAL[],
    p_sample_rate INTEGER,
    p_threshold REAL DEFAULT 0.001,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT[]
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom_ids BIGINT[];
    v_sample_id BIGINT;
    v_time REAL;
    v_stored_count INTEGER := 0;
BEGIN
    -- Only atomize NON-ZERO samples
    FOR i IN 1..array_length(p_samples, 1) LOOP
        IF ABS(p_samples[i]) > p_threshold THEN
            v_time := (i - 1)::REAL / p_sample_rate;
            
            v_sample_id := atomize_audio_sample(
                p_samples[i],
                v_time,
                0,
                p_metadata || jsonb_build_object(
                    'sample_rate', p_sample_rate,
                    'sample_index', i,
                    'sparse', true
                )
            );
            
            v_atom_ids := array_append(v_atom_ids, v_sample_id);
            v_stored_count := v_stored_count + 1;
        END IF;
    END LOOP;
    
    RAISE NOTICE 'Sparse audio: stored % / % samples (%.1f%% compression)',
        v_stored_count, array_length(p_samples, 1),
        100.0 * (1.0 - v_stored_count::REAL / array_length(p_samples, 1));
    
    RETURN v_atom_ids;
END;
$$;

COMMENT ON FUNCTION atomize_audio_sparse(REAL[], INTEGER, REAL, JSONB) IS 
'Sparse audio atomization: skip near-zero samples (silence). Gaps in Hilbert curve = implicit zeros.';
