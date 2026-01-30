CREATE OR REPLACE FUNCTION hartonomous.find_composition(search_text TEXT)
RETURNS TABLE (
    hash BYTEA,
    text TEXT,
    centroid_x DOUBLE PRECISION,
    centroid_y DOUBLE PRECISION,
    centroid_z DOUBLE PRECISION,
    centroid_w DOUBLE PRECISION
)
LANGUAGE sql
STABLE
AS $$
    SELECT hash, text, centroid_x, centroid_y, centroid_z, centroid_w
    FROM hartonomous.compositions
    WHERE LOWER(text) = LOWER(search_text)
    LIMIT 1;
$$;