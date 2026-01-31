-- ==============================================================================
-- Function: Find Similar Trajectories
-- ==============================================================================

CREATE OR REPLACE FUNCTION find_similar_trajectories(
    target_trajectory GEOMETRY,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    relation_id UUID,
    similarity DOUBLE PRECISION
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        r.Id,
        1.0 / (1.0 + ST_HausdorffDistance(p.Trajectory, target_trajectory)) AS similarity
    FROM
        Relation r
    JOIN
        Physicality p ON r.PhysicalityId = p.Id
    WHERE
        p.Trajectory IS NOT NULL
    ORDER BY
        p.Trajectory <-> target_trajectory
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_similar_trajectories IS 'Find Relations with similar spatiotemporal trajectories';