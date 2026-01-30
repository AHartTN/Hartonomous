-- Function: Find nearest atoms to a given 4D point
CREATE OR REPLACE FUNCTION find_nearest_atoms(
    target_x DOUBLE PRECISION,
    target_y DOUBLE PRECISION,
    target_z DOUBLE PRECISION,
    target_w DOUBLE PRECISION,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    id UUID,
    codepoint UINT32,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        a.Id,
        a.Codepoint,
        geodesic_distance_s3(target_x, target_y, target_z, target_w,
                             ST_X(p.Centroid), ST_Y(p.Centroid), ST_Z(p.Centroid), ST_M(p.Centroid)) AS distance
    FROM
        Atom a
    JOIN
        Physicality p ON a.PhysicalityId = p.Id
    ORDER BY
        p.Centroid <-> ST_SetSRID(ST_MakePoint(target_x, target_y, target_z, target_w), 0)
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_nearest_atoms IS 'Find k-nearest atoms to a target 4D point on S³';