-- ==============================================================================
-- VIEWS: Hierarchical queries
-- ==============================================================================

-- View: Full relation tree (recursive)
CREATE OR REPLACE VIEW v_relation_tree AS
WITH RECURSIVE relation_tree AS (
    -- Base case: level 1 relations (sequences of compositions)
    SELECT
        r.hash AS relation_hash,
        r.level,
        rc.position,
        rc.child_hash,
        rc.child_type,
        1 AS depth,
        ARRAY[r.hash] AS path
    FROM
        relations r
        JOIN relation_children rc ON r.hash = rc.relation_hash
    WHERE
        r.level = 1

    UNION ALL

    -- Recursive case: deeper levels
    SELECT
        r.hash AS relation_hash,
        r.level,
        rc.position,
        rc.child_hash,
        rc.child_type,
        rt.depth + 1 AS depth,
        rt.path || r.hash AS path
    FROM
        relations r
        JOIN relation_children rc ON r.hash = rc.relation_hash
        JOIN relation_tree rt ON r.hash = rt.child_hash
    WHERE
        rc.child_type = 'relation'
        AND NOT (r.hash = ANY(rt.path)) -- Prevent cycles
)
SELECT * FROM relation_tree;

COMMENT ON VIEW v_relation_tree IS 'Recursive view of the complete relation hierarchy';