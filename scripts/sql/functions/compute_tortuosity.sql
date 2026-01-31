-- ==============================================================================
-- Function: Compute Tortuosity (Path efficiency on S³)
-- ==============================================================================

CREATE OR REPLACE FUNCTION compute_tortuosity(
    relation_id UUID
)
RETURNS DOUBLE PRECISION
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    start_point GEOMETRY;
    end_point GEOMETRY;
    total_path_length DOUBLE PRECISION := 0;
    euclidean_distance DOUBLE PRECISION;
    prev_point GEOMETRY;
    curr_point GEOMETRY;
    rec RECORD;
BEGIN
    -- 1. Get ordered sequence of centroids for this relation
    FOR rec IN
        SELECT p.Centroid
        FROM RelationSequence rs
        JOIN Composition c ON rs.CompositionId = c.Id
        JOIN Physicality p ON c.PhysicalityId = p.Id
        WHERE rs.RelationId = relation_id
        ORDER BY rs.Ordinal
    LOOP
        curr_point := rec.Centroid;

        IF prev_point IS NOT NULL THEN
            -- Add segment length (Geodesic on S3)
            -- Use PostGIS 3D distance as approximation or custom S3 distance if defined
            -- Since we have geodesic_distance_s3 defined for coordinates, we use it by extracting coords.
            total_path_length := total_path_length + geodesic_distance_s3(
                ST_X(prev_point), ST_Y(prev_point), ST_Z(prev_point), ST_M(prev_point),
                ST_X(curr_point), ST_Y(curr_point), ST_Z(curr_point), ST_M(curr_point)
            );
        ELSE
            start_point := curr_point;
        END IF;

        prev_point := curr_point;
    END LOOP;

    end_point := prev_point;

    IF start_point IS NULL OR end_point IS NULL OR total_path_length = 0 THEN
        RETURN 0.0;
    END IF;

    -- 2. Calculate straight-line distance (Geodesic)
    euclidean_distance := geodesic_distance_s3(
        ST_X(start_point), ST_Y(start_point), ST_Z(start_point), ST_M(start_point),
        ST_X(end_point), ST_Y(end_point), ST_Z(end_point), ST_M(end_point)
    );

    IF euclidean_distance = 0 THEN
        RETURN 0.0;
    END IF;

    -- 3. Tortuosity = Path Length / Straight Line Distance
    RETURN total_path_length / euclidean_distance;
END;
$$;

COMMENT ON FUNCTION compute_tortuosity IS 'Calculates tortuosity of a Relation path on S³ (1.0 = straight line)';