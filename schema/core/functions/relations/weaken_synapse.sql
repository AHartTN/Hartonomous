-- ============================================================================
-- Weaken Synapse
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION weaken_synapse(
    p_source_id BIGINT,
    p_target_id BIGINT,
    p_relation_type TEXT,
    p_decay_rate REAL DEFAULT 0.1
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_relation_type_id BIGINT;
BEGIN
    SELECT atom_id INTO v_relation_type_id
    FROM atom
    WHERE canonical_text = p_relation_type
      AND metadata->>'type' = 'relation_type';
    
    IF NOT FOUND THEN
        RETURN;
    END IF;
    
    UPDATE atom_relation
    SET weight = GREATEST(weight - p_decay_rate, 0.0)
    WHERE source_atom_id = p_source_id
      AND target_atom_id = p_target_id
      AND relation_type_id = v_relation_type_id;
END;
$$;

COMMENT ON FUNCTION weaken_synapse(BIGINT, BIGINT, TEXT, REAL) IS 
'Explicitly weaken synaptic connection.';
