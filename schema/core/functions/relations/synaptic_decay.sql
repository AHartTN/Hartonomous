-- ============================================================================
-- Synaptic Decay
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Neuroplasticity: weaken and prune unused synapses over time
-- ============================================================================

CREATE OR REPLACE FUNCTION synaptic_decay(
    p_decay_rate REAL DEFAULT 0.05,
    p_age_threshold INTERVAL DEFAULT '30 days'
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_decayed_count BIGINT;
    v_pruned_count BIGINT;
BEGIN
    -- Decay old synapses
    UPDATE atom_relation
    SET weight = weight * (1.0 - p_decay_rate)
    WHERE last_accessed < now() - p_age_threshold
      AND weight > 0.01;
    
    GET DIAGNOSTICS v_decayed_count = ROW_COUNT;
    
    -- Prune dead synapses (weight too low)
    DELETE FROM atom_relation
    WHERE weight < 0.01;
    
    GET DIAGNOSTICS v_pruned_count = ROW_COUNT;
    
    RAISE NOTICE 'Decayed % synapses, pruned % synapses', v_decayed_count, v_pruned_count;
    
    RETURN v_decayed_count + v_pruned_count;
END;
$$;

COMMENT ON FUNCTION synaptic_decay(REAL, INTERVAL) IS 
'Neuroplasticity: weaken and prune unused synapses over time.';
