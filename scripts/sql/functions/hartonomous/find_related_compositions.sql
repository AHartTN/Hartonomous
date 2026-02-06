-- ==============================================================================
-- FIND RELATED COMPOSITIONS: Wrapper for Semantic Search (Hash-based)
-- ==============================================================================

CREATE OR REPLACE FUNCTION find_related_compositions(
    query_text TEXT,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    composition_id UUID,
    text TEXT,
    distance DOUBLE PRECISION
)
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    query_id UUID;
    query_centroid GEOMETRY;
BEGIN
    -- 1. Compute ID from Text (Fast, Deterministic)
    -- blake3_hash returns 16-byte bytea, cast directly to UUID
    query_id := blake3_hash(query_text)::uuid;

    -- 2. Get Centroid (Index Scan on Primary Key)
    SELECT p.Centroid INTO query_centroid
    FROM Composition c
    JOIN Physicality p ON c.PhysicalityId = p.Id
    WHERE c.Id = query_id;

    IF query_centroid IS NULL THEN
        RETURN;
    END IF;

    -- 3. Execute Geometric Search
    RETURN QUERY
    SELECT
        s.composition_id,
        v.reconstructed_text,
        s.distance
    FROM
        semantic_search_geometric(query_centroid, max_results) s
    JOIN
        v_composition_text v ON s.composition_id = v.composition_id;
END;
$$;

COMMENT ON FUNCTION find_related_compositions IS 'Finds related compositions using hash-lookup and geometric search';