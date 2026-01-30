CREATE OR REPLACE FUNCTION hartonomous.repair_inconsistencies()
RETURNS TABLE (
    issue TEXT,
    fixed_count INTEGER
)
LANGUAGE plpgsql
AS $$
DECLARE
    orphaned_comp_atoms INTEGER;
    orphaned_rel_children INTEGER;
BEGIN
    -- Fix orphaned composition_atoms (compositions that don't exist)
    WITH deleted AS (
        DELETE FROM hartonomous.composition_atoms ca
        WHERE NOT EXISTS (
            SELECT 1 FROM hartonomous.compositions c WHERE c.hash = ca.composition_hash
        )
        RETURNING *
    )
    SELECT COUNT(*)::INTEGER INTO orphaned_comp_atoms FROM deleted;

    IF orphaned_comp_atoms > 0 THEN
        RETURN QUERY SELECT 'Orphaned composition_atoms'::TEXT, orphaned_comp_atoms;
    END IF;

    -- Fix orphaned relation_children
    WITH deleted AS (
        DELETE FROM hartonomous.relation_children rc
        WHERE NOT EXISTS (
            SELECT 1 FROM hartonomous.relations r WHERE r.hash = rc.relation_hash
        )
        RETURNING *
    )
    SELECT COUNT(*)::INTEGER INTO orphaned_rel_children FROM deleted;

    IF orphaned_rel_children > 0 THEN
        RETURN QUERY SELECT 'Orphaned relation_children'::TEXT, orphaned_rel_children;
    END IF;

    -- If nothing to fix
    IF orphaned_comp_atoms = 0 AND orphaned_rel_children = 0 THEN
        RETURN QUERY SELECT 'No issues found'::TEXT, 0::INTEGER;
    END IF;
END;
$$;