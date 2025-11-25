-- ============================================================================
-- Prune by Importance (Model Compression)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Magnitude-based pruning - remove low-importance connections
-- ============================================================================

CREATE OR REPLACE FUNCTION prune_by_importance(
    p_weight_threshold REAL DEFAULT 0.1,
    p_reference_threshold BIGINT DEFAULT 10
)
RETURNS TABLE(
    pruned_relations BIGINT,
    pruned_atoms BIGINT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_pruned_relations BIGINT;
    v_pruned_atoms BIGINT;
BEGIN
    -- Prune weak relations
    DELETE FROM atom_relation
    WHERE weight < p_weight_threshold
      AND importance < 0.3;
    
    GET DIAGNOSTICS v_pruned_relations = ROW_COUNT;
    
    -- Prune unreferenced atoms (garbage collection)
    DELETE FROM atom
    WHERE reference_count < p_reference_threshold
      AND atom_id NOT IN (
          SELECT DISTINCT source_atom_id FROM atom_relation
          UNION
          SELECT DISTINCT target_atom_id FROM atom_relation
          UNION
          SELECT DISTINCT parent_atom_id FROM atom_composition
          UNION
          SELECT DISTINCT component_atom_id FROM atom_composition
      );
    
    GET DIAGNOSTICS v_pruned_atoms = ROW_COUNT;
    
    RETURN QUERY SELECT v_pruned_relations, v_pruned_atoms;
END;
$$;

COMMENT ON FUNCTION prune_by_importance(REAL, BIGINT) IS 
'Magnitude pruning: remove low-weight relations and unreferenced atoms.
Model compression: reduces memory footprint without significant accuracy loss.
Use after training to compress knowledge graph.';
