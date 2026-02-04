-- ==============================================================================
-- Function: find_gravitational_centers
-- Identifies dense clusters of meaning in 4D space.
-- This is the core of the "Physics of Truth".
-- ==============================================================================

CREATE OR REPLACE FUNCTION find_gravitational_centers(
    min_mass DOUBLE PRECISION DEFAULT 1000.0,
    radius DOUBLE PRECISION DEFAULT 0.05
)
RETURNS TABLE (
    cluster_centroid GEOMETRY(POINTZM, 0),
    total_mass DOUBLE PRECISION,
    member_count BIGINT
)
LANGUAGE sql
STABLE
AS $$
    WITH candidates AS (
        SELECT 
            p.centroid,
            (rr.ratingvalue * LOG(rr.observations + 1)) as mass
        FROM hartonomous.physicality p
        JOIN hartonomous.relation r ON r.physicalityid = p.id
        JOIN hartonomous.relationrating rr ON rr.relationid = r.id
    )
    SELECT 
        ST_Centroid(ST_Collect(c1.centroid)) as cluster_centroid,
        SUM(c1.mass) as total_mass,
        COUNT(*) as member_count
    FROM candidates c1
    JOIN candidates c2 ON ST_3DDistance(c1.centroid, c2.centroid) < radius
    GROUP BY c1.centroid
    HAVING SUM(c1.mass) >= min_mass
    ORDER BY total_mass DESC;
$$;

COMMENT ON FUNCTION find_gravitational_centers IS 'Identifies high-density clusters of semantic edges representing stable truths.';
