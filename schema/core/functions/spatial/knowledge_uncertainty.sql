-- ============================================================================
-- Knowledge Uncertainty
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Measure uncertainty via neighbor distance variance (Heisenberg analog)
-- ============================================================================

CREATE OR REPLACE FUNCTION knowledge_uncertainty(p_atom_id BIGINT)
RETURNS REAL
LANGUAGE plpgsql
AS $$
DECLARE
    v_variance REAL;
    v_center GEOMETRY;
BEGIN
    SELECT spatial_key INTO v_center
    FROM atom WHERE atom_id = p_atom_id;
    
    IF v_center IS NULL THEN
        RETURN NULL;
    END IF;
    
    -- Variance of neighbor distances (uncertainty measure)
    SELECT VARIANCE(ST_Distance(a.spatial_key, v_center))
    INTO v_variance
    FROM atom a
    WHERE ST_DWithin(a.spatial_key, v_center, 1.0)
      AND a.atom_id != p_atom_id;
    
    RETURN COALESCE(v_variance, 0.0);
END;
$$;

COMMENT ON FUNCTION knowledge_uncertainty(BIGINT) IS 
'Measure knowledge uncertainty via neighbor distance variance. High = uncertain/general, Low = precise/specific.';
