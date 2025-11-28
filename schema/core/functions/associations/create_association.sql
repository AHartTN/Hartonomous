-- ============================================================================
-- Create Association Function  
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION create_association(
    p_atom_id BIGINT,
    p_landmark_id BIGINT,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_association_id BIGINT;
    v_distance DOUBLE PRECISION;
BEGIN
    -- Calculate 3D distance between atom and landmark
    SELECT ST_3DDistance(
        (SELECT spatial_key FROM atom WHERE atom_id = p_atom_id),
        (SELECT spatial_position FROM landmark WHERE landmark_id = p_landmark_id)
    ) INTO v_distance;
    
    -- Insert or update association
    INSERT INTO atom_landmark_association (
        atom_id,
        landmark_id,
        distance,
        metadata,
        created_at
    )
    VALUES (
        p_atom_id,
        p_landmark_id,
        v_distance,
        p_metadata,
        now()
    )
    ON CONFLICT (atom_id, landmark_id)
    DO UPDATE SET
        distance = EXCLUDED.distance,
        metadata = atom_landmark_association.metadata || EXCLUDED.metadata
    RETURNING association_id INTO v_association_id;
    
    RETURN v_association_id;
END;
$$;

COMMENT ON FUNCTION create_association(BIGINT, BIGINT, JSONB) IS 
'Create association between atom and landmark with computed 3D distance.';
