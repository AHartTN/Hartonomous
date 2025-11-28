-- ============================================================================
-- Store Landmark Function
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION store_landmark(
    p_landmark_name TEXT,
    p_x DOUBLE PRECISION,
    p_y DOUBLE PRECISION,
    p_z DOUBLE PRECISION,
    p_weight REAL DEFAULT 1.0,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_landmark_id BIGINT;
BEGIN
    -- Insert or update landmark
    INSERT INTO landmark (
        landmark_name,
        spatial_position,
        weight,
        metadata,
        created_at
    )
    VALUES (
        p_landmark_name,
        ST_MakePoint(p_x, p_y, p_z),
        p_weight,
        p_metadata,
        now()
    )
    ON CONFLICT (landmark_name)
    DO UPDATE SET
        weight = EXCLUDED.weight,
        metadata = landmark.metadata || EXCLUDED.metadata,
        spatial_position = EXCLUDED.spatial_position
    RETURNING landmark_id INTO v_landmark_id;
    
    RETURN v_landmark_id;
END;
$$;

COMMENT ON FUNCTION store_landmark(TEXT, DOUBLE PRECISION, DOUBLE PRECISION, DOUBLE PRECISION, REAL, JSONB) IS 
'Store or update a landmark with 3D spatial position. Landmarks are reference points in semantic space.';
