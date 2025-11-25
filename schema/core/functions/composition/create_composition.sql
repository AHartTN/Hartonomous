-- ============================================================================
-- Create Composition Relationship
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION create_composition(
    p_parent_id BIGINT,
    p_component_id BIGINT,
    p_sequence_index BIGINT,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_composition_id BIGINT;
BEGIN
    INSERT INTO atom_composition (
        parent_atom_id,
        component_atom_id,
        sequence_index,
        metadata
    )
    VALUES (
        p_parent_id,
        p_component_id,
        p_sequence_index,
        p_metadata
    )
    ON CONFLICT (parent_atom_id, component_atom_id, sequence_index)
    DO UPDATE SET metadata = atom_composition.metadata || EXCLUDED.metadata
    RETURNING composition_id INTO v_composition_id;
    
    RETURN v_composition_id;
END;
$$;

COMMENT ON FUNCTION create_composition(BIGINT, BIGINT, BIGINT, JSONB) IS 
'Create hierarchical composition: parent contains component at sequence position.';
