-- ============================================================================
-- Batch composition creation
-- Creates multiple parent->child relationships in one call
-- ============================================================================

CREATE OR REPLACE FUNCTION create_composition_batch(
    p_parent_atom_id bigint,
    component_data jsonb[] -- Array of {component_id: bigint, sequence_idx: bigint}
)
RETURNS int AS $$
DECLARE
    comp jsonb;
    inserted_count int := 0;
BEGIN
    -- Temporarily disable reference counting trigger for batch operation
    -- We'll batch update reference counts after insert
    ALTER TABLE atom_composition DISABLE TRIGGER trigger_increment_refcount;
    
    -- Insert all compositions in one statement
    INSERT INTO atom_composition (
        parent_atom_id,
        component_atom_id,
        sequence_index,
        metadata
    )
    SELECT 
        p_parent_atom_id,
        (elem->>'component_id')::bigint,
        (elem->>'sequence_idx')::bigint,
        '{}'::jsonb
    FROM unnest(component_data) AS elem
    ON CONFLICT (parent_atom_id, component_atom_id, sequence_index) 
    DO NOTHING;
    
    GET DIAGNOSTICS inserted_count = ROW_COUNT;
    
    -- Batch update reference counts - one UPDATE per unique atom instead of per row
    -- This is MUCH faster than 5000 individual trigger executions
    UPDATE atom
    SET reference_count = reference_count + refcount_delta
    FROM (
        SELECT (elem->>'component_id')::bigint as component_atom_id, COUNT(*) as refcount_delta
        FROM unnest(component_data) AS elem
        GROUP BY (elem->>'component_id')::bigint
    ) AS deltas
    WHERE atom.atom_id = deltas.component_atom_id;
    
    -- Re-enable trigger for normal operations
    ALTER TABLE atom_composition ENABLE TRIGGER trigger_increment_refcount;
    
    RETURN inserted_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION create_composition_batch IS
'Batch insert compositions - much faster than individual create_composition() calls.';
