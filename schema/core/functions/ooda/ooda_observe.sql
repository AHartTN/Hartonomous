-- ============================================================================
-- OODA Observe Phase
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Collect performance metrics and identify optimization opportunities
-- ============================================================================

CREATE OR REPLACE FUNCTION ooda_observe()
RETURNS TABLE(issue TEXT, metric REAL, atom_id BIGINT)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    
    -- Heavy atoms (high reference_count, potential optimization target)
    SELECT
        'heavy_atom'::TEXT AS issue,
        a.reference_count::REAL AS metric,
        a.atom_id
    FROM atom a
    WHERE a.reference_count > 1000000
    ORDER BY a.reference_count DESC
    LIMIT 10
    
    UNION ALL
    
    -- Atoms without spatial positions (need positioning)
    SELECT
        'missing_position'::TEXT,
        COUNT(*)::REAL,
        MIN(a.atom_id)
    FROM atom a
    WHERE a.spatial_key IS NULL
    
    UNION ALL
    
    -- Weak synapses (candidates for pruning)
    SELECT
        'weak_synapse'::TEXT,
        AVG(ar.weight)::REAL,
        ar.source_atom_id
    FROM atom_relation ar
    GROUP BY ar.source_atom_id
    HAVING AVG(ar.weight) < 0.1
    LIMIT 10
    
    UNION ALL
    
    -- Old unused relations
    SELECT
        'stale_relation'::TEXT,
        EXTRACT(EPOCH FROM (now() - ar.last_accessed))::REAL / 86400.0 AS days,
        ar.source_atom_id
    FROM atom_relation ar
    WHERE ar.last_accessed < now() - INTERVAL '90 days'
    LIMIT 10
    
    UNION ALL
    
    -- Spatial drift candidates (positions need recalculation)
    SELECT
        'spatial_drift'::TEXT,
        drift_magnitude,
        d.atom_id
    FROM detect_spatial_drift() d
    LIMIT 10
    
    UNION ALL
    
    -- Non-orthogonal basis (needs Gram-Schmidt)
    SELECT
        'non_orthogonal_basis'::TEXT,
        COUNT(*)::REAL,
        MIN(atom_id)
    FROM atom
    WHERE metadata->>'basis_vector' = 'true'
      AND metadata->>'orthogonalized' != 'true'
    HAVING COUNT(*) >= 3;
    
END;
$$;

COMMENT ON FUNCTION ooda_observe() IS 
'OBSERVE phase: collect performance metrics and identify issues including geometric problems.';
