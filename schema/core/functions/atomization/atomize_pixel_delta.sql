-- ============================================================================
-- Delta Encoding via Hilbert Proximity
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Store color delta from previous Hilbert position instead of absolute RGB
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_pixel_delta(
    p_prev_atom_id BIGINT,
    p_r_delta SMALLINT,
    p_g_delta SMALLINT,
    p_b_delta SMALLINT,
    p_x INTEGER,
    p_y INTEGER,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_prev_r INTEGER;
    v_prev_g INTEGER;
    v_prev_b INTEGER;
    v_new_r INTEGER;
    v_new_g INTEGER;
    v_new_b INTEGER;
    v_atom_id BIGINT;
    v_delta_bytes BYTEA;
BEGIN
    -- Get previous pixel color
    SELECT 
        (metadata->>'r')::INTEGER,
        (metadata->>'g')::INTEGER,
        (metadata->>'b')::INTEGER
    INTO v_prev_r, v_prev_g, v_prev_b
    FROM atom
    WHERE atom_id = p_prev_atom_id;
    
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Previous atom % not found', p_prev_atom_id;
    END IF;
    
    -- Compute absolute RGB from delta
    v_new_r := GREATEST(0, LEAST(255, v_prev_r + p_r_delta));
    v_new_g := GREATEST(0, LEAST(255, v_prev_g + p_g_delta));
    v_new_b := GREATEST(0, LEAST(255, v_prev_b + p_b_delta));
    
    -- Pack delta as 3 bytes (much smaller than full RGB storage)
    v_delta_bytes := int2send(p_r_delta) ||
                     int2send(p_g_delta) ||
                     int2send(p_b_delta);
    
    v_atom_id := atomize_value(
        v_delta_bytes,
        '?(' || p_r_delta || ',' || p_g_delta || ',' || p_b_delta || ')?RGB(' || v_new_r || ',' || v_new_g || ',' || v_new_b || ')',
        p_metadata || jsonb_build_object(
            'modality', 'pixel_delta',
            'r', v_new_r,
            'g', v_new_g,
            'b', v_new_b,
            'r_delta', p_r_delta,
            'g_delta', p_g_delta,
            'b_delta', p_b_delta,
            'x', p_x,
            'y', p_y,
            'prev_atom_id', p_prev_atom_id,
            'encoding', 'delta'
        )
    );
    
    UPDATE atom 
    SET spatial_key = ST_MakePoint(v_new_r, v_new_g, v_new_b)
    WHERE atom_id = v_atom_id;
    
    RETURN v_atom_id;
END;
$$;

COMMENT ON FUNCTION atomize_pixel_delta(BIGINT, SMALLINT, SMALLINT, SMALLINT, INTEGER, INTEGER, JSONB) IS 
'Delta encoding: store RGB change from previous Hilbert-adjacent pixel.
Exploits spatial locality - similar colors are Hilbert-adjacent.';
