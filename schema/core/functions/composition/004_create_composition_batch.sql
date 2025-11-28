-- ============================================================================
-- Batch composition creation
-- Creates multiple parent->child relationships in one call
-- ============================================================================

CREATE OR REPLACE FUNCTION create_composition_batch(
    parent_atom_id bigint,
    component_data jsonb[] -- Array of {component_id: bigint, sequence_idx: bigint}
)
RETURNS int AS $$
DECLARE
    comp jsonb;
    inserted_count int := 0;
BEGIN
    -- Insert all compositions in one statement
    INSERT INTO atom_composition (
        parent_atom_id,
        component_atom_id,
        sequence_index,
        composition_metadata
    )
    SELECT 
        parent_atom_id,
        (elem->>'component_id')::bigint,
        (elem->>'sequence_idx')::bigint,
        '{}'::jsonb
    FROM unnest(component_data) AS elem
    ON CONFLICT (parent_atom_id, component_atom_id, sequence_index) 
    DO NOTHING;
    
    GET DIAGNOSTICS inserted_count = ROW_COUNT;
    RETURN inserted_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION create_composition_batch IS
'Batch insert compositions - much faster than individual create_composition() calls.';
