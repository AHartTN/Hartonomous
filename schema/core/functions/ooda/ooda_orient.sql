-- ============================================================================
-- OODA Orient Phase
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Analyze root causes and generate recommendations
-- ============================================================================

CREATE OR REPLACE FUNCTION ooda_orient(
    p_issue TEXT,
    p_metric REAL,
    p_atom_id BIGINT DEFAULT NULL
)
RETURNS TABLE(root_cause TEXT, recommendation TEXT)
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_issue = 'heavy_atom' THEN
        RETURN QUERY
        SELECT
            ('Atom ' || p_atom_id || ' referenced ' || p_metric || ' times')::TEXT,
            'Materialize spatial position or add specialized index'::TEXT;
            
    ELSIF p_issue = 'missing_position' THEN
        RETURN QUERY
        SELECT
            (p_metric || ' atoms lack spatial positions')::TEXT,
            'Run batch spatial positioning: SELECT compute_spatial_positions_batch(ARRAY_AGG(atom_id)) FROM atom WHERE spatial_key IS NULL'::TEXT;
            
    ELSIF p_issue = 'weak_synapse' THEN
        RETURN QUERY
        SELECT
            'Synapse weight decayed below threshold'::TEXT,
            'Prune weak synapses: SELECT synaptic_decay(0.05, ''30 days'')'::TEXT;
            
    ELSIF p_issue = 'stale_relation' THEN
        RETURN QUERY
        SELECT
            ('Relations unused for ' || p_metric || ' days')::TEXT,
            'Run synaptic decay to prune old connections'::TEXT;
            
    ELSIF p_issue = 'spatial_drift' THEN
        RETURN QUERY
        SELECT
            ('Spatial position drifted by ' || p_metric || ' units')::TEXT,
            'Recalculate position using trilateration or natural neighbor interpolation'::TEXT;
            
    ELSIF p_issue = 'non_orthogonal_basis' THEN
        RETURN QUERY
        SELECT
            (p_metric || ' basis vectors are not orthogonal')::TEXT,
            'Apply Gram-Schmidt orthogonalization'::TEXT;
            
    ELSE
        RETURN QUERY
        SELECT
            'Unknown issue type'::TEXT,
            'Manual investigation required'::TEXT;
    END IF;
END;
$$;

COMMENT ON FUNCTION ooda_orient(TEXT, REAL, BIGINT) IS 
'ORIENT phase: analyze root causes of observed issues including geometric problems.';
