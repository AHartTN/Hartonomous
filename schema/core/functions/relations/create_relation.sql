-- ============================================================================
-- Create Relation Function
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION create_relation(
    p_source_id BIGINT,
    p_target_id BIGINT,
    p_relation_type TEXT,
    p_weight REAL DEFAULT 0.5,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_relation_type_id BIGINT;
    v_relation_id BIGINT;
BEGIN
    -- Validate weight
    IF p_weight < 0.0 OR p_weight > 1.0 THEN
        RAISE EXCEPTION 'Weight must be between 0.0 and 1.0, got %', p_weight;
    END IF;
    
    -- Get or create relation type atom
    v_relation_type_id := atomize_value(
        convert_to(p_relation_type, 'UTF8'),
        p_relation_type,
        jsonb_build_object('type', 'relation_type')
    );
    
    -- Create or update relation
    INSERT INTO atom_relation (
        source_atom_id,
        target_atom_id,
        relation_type_id,
        weight,
        confidence,
        importance,
        metadata,
        last_accessed
    )
    VALUES (
        p_source_id,
        p_target_id,
        v_relation_type_id,
        p_weight,
        0.5,  -- Default confidence
        0.5,  -- Default importance
        p_metadata,
        now()
    )
    ON CONFLICT (source_atom_id, target_atom_id, relation_type_id)
    DO UPDATE SET 
        weight = EXCLUDED.weight,
        metadata = atom_relation.metadata || EXCLUDED.metadata,
        last_accessed = now()
    RETURNING relation_id INTO v_relation_id;
    
    RETURN v_relation_id;
END;
$$;

COMMENT ON FUNCTION create_relation(BIGINT, BIGINT, TEXT, REAL, JSONB) IS 
'Create semantic relation between atoms. Relation type is itself an atom.';
