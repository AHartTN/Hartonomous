-- ============================================================================
-- OODA Decide Phase
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Generate executable optimization hypothesis
-- ============================================================================

CREATE OR REPLACE FUNCTION ooda_decide(p_recommendation TEXT)
RETURNS TEXT
LANGUAGE plpgsql
AS $$
DECLARE
    v_hypothesis TEXT;
BEGIN
    -- Parse recommendation and generate executable SQL
    IF p_recommendation LIKE '%batch spatial positioning%' THEN
        v_hypothesis := 'SELECT compute_spatial_positions_batch(ARRAY(SELECT atom_id FROM atom WHERE spatial_key IS NULL LIMIT 1000))';
        
    ELSIF p_recommendation LIKE '%synaptic_decay%' OR p_recommendation LIKE '%Prune weak%' THEN
        v_hypothesis := 'SELECT synaptic_decay(0.05, ''30 days'')';
        
    ELSIF p_recommendation LIKE '%Materialize spatial%' THEN
        v_hypothesis := 'UPDATE atom SET spatial_key = compute_spatial_position(atom_id) WHERE reference_count > 1000000 AND spatial_key IS NULL';
        
    ELSIF p_recommendation LIKE '%CREATE INDEX%' THEN
        v_hypothesis := p_recommendation;
        
    ELSE
        v_hypothesis := NULL;  -- Unknown recommendation
    END IF;
    
    RETURN v_hypothesis;
END;
$$;

COMMENT ON FUNCTION ooda_decide(TEXT) IS 
'DECIDE phase: generate executable optimization hypothesis.';
