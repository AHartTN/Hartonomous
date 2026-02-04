-- ==============================================================================
-- Function: compute_trajectory_tortuosity
-- Measures the "complexity" or "curviness" of a meaning trajectory.
-- High tortuosity indicates high semantic complexity.
-- ==============================================================================

CREATE OR REPLACE FUNCTION compute_trajectory_tortuosity(
    trajectory GEOMETRY(LINESTRINGZM, 0)
)
RETURNS DOUBLE PRECISION
LANGUAGE plpgsql
IMMUTABLE STRICT
AS $$
DECLARE
    total_length DOUBLE PRECISION;
    straight_line DOUBLE PRECISION;
    p_start GEOMETRY(POINTZM, 0);
    p_end GEOMETRY(POINTZM, 0);
BEGIN
    -- Length along the manifold
    total_length := ST_Length(trajectory);
    
    IF total_length = 0 THEN
        RETURN 1.0;
    END IF;

    -- Straight line distance between endpoints
    p_start := ST_StartPoint(trajectory);
    p_end := ST_EndPoint(trajectory);
    
    straight_line := SQRT(
        POW(ST_X(p_end) - ST_X(p_start), 2) +
        POW(ST_Y(p_end) - ST_Y(p_start), 2) +
        POW(ST_Z(p_end) - ST_Z(p_start), 2) +
        POW(ST_M(p_end) - ST_M(p_start), 2)
    );

    IF straight_line = 0 THEN
        RETURN total_length; -- Loop back
    END IF;

    RETURN total_length / straight_line;
END;
$$;

COMMENT ON FUNCTION compute_trajectory_tortuosity IS 'Measures the complexity of a semantic trajectory (total distance / straight distance).';
