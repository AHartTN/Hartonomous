-- ============================================================================
-- Delete Composition Relationship
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION delete_composition(
    p_composition_id BIGINT
)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
DECLARE
    v_deleted BOOLEAN;
BEGIN
    DELETE FROM atom_composition
    WHERE composition_id = p_composition_id;
    
    GET DIAGNOSTICS v_deleted = FOUND;
    RETURN v_deleted;
END;
$$;

COMMENT ON FUNCTION delete_composition(BIGINT) IS 
'Delete composition relationship. Automatically decrements reference count via trigger.';
