-- ============================================================================
-- Reinforce Synapse (Hebbian Learning)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- "Neurons that fire together, wire together"
-- ============================================================================

CREATE OR REPLACE FUNCTION reinforce_synapse(
    p_source_id BIGINT,
    p_target_id BIGINT,
    p_relation_type TEXT DEFAULT 'semantic_correlation',
    p_learning_rate REAL DEFAULT 0.05
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_relation_type_id BIGINT;
BEGIN
    -- Get relation type atom
    v_relation_type_id := atomize_value(
        convert_to(p_relation_type, 'UTF8'),
        p_relation_type,
        jsonb_build_object('type', 'relation_type')
    );
    
    -- Strengthen existing synapse (cap at 1.0)
    UPDATE atom_relation
    SET 
        weight = LEAST(weight + p_learning_rate, 1.0),
        importance = importance * 1.02,
        last_accessed = now()
    WHERE source_atom_id = p_source_id
      AND target_atom_id = p_target_id
      AND relation_type_id = v_relation_type_id;
    
    -- Create new synapse if none exists
    IF NOT FOUND THEN
        INSERT INTO atom_relation (
            source_atom_id,
            target_atom_id,
            relation_type_id,
            weight,
            last_accessed
        )
        VALUES (
            p_source_id,
            p_target_id,
            v_relation_type_id,
            p_learning_rate,
            now()
        );
    END IF;
END;
$$;

COMMENT ON FUNCTION reinforce_synapse(BIGINT, BIGINT, TEXT, REAL) IS 
'Hebbian learning: strengthen synaptic connection when atoms co-activate.';
