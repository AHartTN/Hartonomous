-- ==============================================================================
-- Function: Validate Relation Child
-- ==============================================================================

CREATE OR REPLACE FUNCTION validate_relation_child(
    relation_id UUID,
    child_composition_id UUID
)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM RelationSequence rs
        WHERE rs.RelationId = relation_id
          AND rs.CompositionId = child_composition_id
    );
$$;

COMMENT ON FUNCTION validate_relation_child IS 'Check if a Composition is part of a Relation';