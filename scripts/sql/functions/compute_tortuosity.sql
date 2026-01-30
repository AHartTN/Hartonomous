-- ==============================================================================
-- FUNCTIONS: Hierarchical operations
-- ==============================================================================

-- Function: Compute trajectory tortuosity
CREATE OR REPLACE FUNCTION compute_tortuosity(relation_hash_param BYTEA)
RETURNS DOUBLE PRECISION
LANGUAGE plpgsql
AS $$
DECLARE
    total_dist DOUBLE PRECISION;
    start_point RECORD;
    end_point RECORD;
    straight_dist DOUBLE PRECISION;
BEGIN
    -- Get total trajectory distance
    SELECT geometric_length INTO total_dist
    FROM relations
    WHERE hash = relation_hash_param;

    -- Get start and end points
    SELECT centroid_x, centroid_y, centroid_z, centroid_w INTO start_point
    FROM relations
    WHERE hash = (
        SELECT child_hash
        FROM relation_children
        WHERE relation_hash = relation_hash_param
        ORDER BY position
        LIMIT 1
    );

    SELECT centroid_x, centroid_y, centroid_z, centroid_w INTO end_point
    FROM relations
    WHERE hash = (
        SELECT child_hash
        FROM relation_children
        WHERE relation_hash = relation_hash_param
        ORDER BY position DESC
        LIMIT 1
    );

    -- Compute straight-line distance
    straight_dist := SQRT(
        POWER(end_point.centroid_x - start_point.centroid_x, 2) +
        POWER(end_point.centroid_y - start_point.centroid_y, 2) +
        POWER(end_point.centroid_z - start_point.centroid_z, 2) +
        POWER(end_point.centroid_w - start_point.centroid_w, 2)
    );

    -- Avoid division by zero
    IF straight_dist < 1e-10 THEN
        RETURN 1.0;
    END IF;

    RETURN total_dist / straight_dist;
END;
$$;

COMMENT ON FUNCTION compute_tortuosity IS 'Compute trajectory tortuosity (total_dist / straight_dist)';