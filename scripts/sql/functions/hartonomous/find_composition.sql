CREATE OR REPLACE FUNCTION hartonomous.find_composition(search_text TEXT)
RETURNS TABLE (
    id UUID,
    text TEXT,
    centroid_x DOUBLE PRECISION,
    centroid_y DOUBLE PRECISION,
    centroid_z DOUBLE PRECISION,
    centroid_w DOUBLE PRECISION
)
LANGUAGE plpgsql
STABLE
AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.Id,
        v.reconstructed_text,
        ST_X(p.Centroid),
        ST_Y(p.Centroid),
        ST_Z(p.Centroid),
        ST_M(p.Centroid)
    FROM
        v_composition_text v
    JOIN
        Composition c ON v.composition_id = c.Id
    JOIN
        Physicality p ON c.PhysicalityId = p.Id
    WHERE
        v.reconstructed_text = search_text;
END;
$$;

COMMENT ON FUNCTION hartonomous.find_composition IS 'Find composition by exact text match (using view)';