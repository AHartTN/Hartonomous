-- ==============================================================================
-- OPERATOR: <=>
-- 4D S³ geodesic distance operator for POINTZM geometries.
-- ==============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_operator
        WHERE oprname = '<=>'
          AND oprleft  = 'geometry'::regtype
          AND oprright = 'geometry'::regtype
    ) THEN
        CREATE OPERATOR <=> (
            LEFTARG = GEOMETRY,
            RIGHTARG = GEOMETRY,
            PROCEDURE = geodesic_distance_s3
        );
    END IF;
END $$;

COMMENT ON OPERATOR <=> IS
'Returns the 4D geodesic distance between two POINTZM geometries on S³.';
