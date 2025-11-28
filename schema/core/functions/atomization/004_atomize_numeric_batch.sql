-- ============================================================================
-- Batch atomization for numeric values
-- Processes 1000+ weights per SQL call instead of 1 call per weight
-- Expected speedup: 100-200x for quantized models
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_numeric_batch(
    weights numeric[],
    metadata_template jsonb DEFAULT '{}'::jsonb
)
RETURNS TABLE(
    weight_value numeric,
    atom_id bigint
) AS $$
DECLARE
    weight numeric;
    weight_hash bytea;
    weight_atom_id bigint;
    weight_metadata jsonb;
BEGIN
    -- Process each unique weight value
    FOR weight IN SELECT DISTINCT unnest(weights) LOOP
        -- Create hash from numeric value
        weight_hash := digest(weight::text, 'sha256');
        
        -- Merge weight value into metadata
        weight_metadata := metadata_template || jsonb_build_object('value', weight);
        
        -- Atomize this weight (will deduplicate if exists)
        SELECT atomize_value(
            weight_hash,
            weight::text,
            weight_metadata
        ) INTO weight_atom_id;
        
        -- Return mapping
        RETURN QUERY SELECT weight, weight_atom_id;
    END LOOP;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION atomize_numeric_batch IS
'Batch atomization for numeric weight values - processes array of weights and returns atom_id for each unique value. Dramatically faster than per-weight atomization.';
