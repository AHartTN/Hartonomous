-- ==============================================================================
-- FUNCTION: find_nearest_atoms
-- Finds the k-nearest atoms to a target 4D POINTZM on S³.
-- ==============================================================================

CREATE OR REPLACE FUNCTION find_nearest_atoms(
    target GEOMETRY(POINTZM, 0),
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    id UUID,
    codepoint INT,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        a.Id,
        a.Codepoint,
        geodesic_distance_s3(target, p.Centroid) AS distance
    FROM Atom a
    JOIN Physicality p ON a.PhysicalityId = p.Id
    ORDER BY p.Centroid <-> target
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_nearest_atoms IS
'Find k-nearest atoms to a target 4D POINTZM on S³ using PostGIS distance ordering.';
