-- ============================================================================
-- Delete Relation
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION delete_relation(
    p_relation_id BIGINT
)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
DECLARE
    v_deleted BOOLEAN;
BEGIN
    DELETE FROM atom_relation
    WHERE relation_id = p_relation_id;
    
    GET DIAGNOSTICS v_deleted = ROW_COUNT;
    RETURN v_deleted > 0;
END;
$$;

COMMENT ON FUNCTION delete_relation(BIGINT) IS 
'Delete semantic relation by relation_id.';
