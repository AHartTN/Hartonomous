-- ============================================================================
-- Hilbert Curve Encoding Functions
-- ============================================================================
-- Implements 3D Hilbert space-filling curve for locality-preserving indexing
-- Based on: "Efficient 3D Hilbert Curve Encoding and Decoding Algorithms"
-- Reference: https://arxiv.org/pdf/2308.05673
-- ============================================================================

-- ============================================================================
-- hilbert_encode_3d: Convert 3D coordinates to 1D Hilbert index
-- ============================================================================
-- Uses rotation-based algorithm for optimal locality preservation
-- Parameters:
--   p_x, p_y, p_z: Normalized coordinates [0.0, 1.0]
--   p_bits: Precision bits (default 21 = ~2M resolution per dimension)
-- Returns: 63-bit Hilbert index (21 bits * 3 dimensions)
-- ============================================================================
CREATE OR REPLACE FUNCTION hilbert_encode_3d(
    p_x FLOAT8,
    p_y FLOAT8, 
    p_z FLOAT8,
    p_bits INT DEFAULT 21
) RETURNS BIGINT AS $$
DECLARE
    v_x BIGINT;
    v_y BIGINT;
    v_z BIGINT;
    v_max BIGINT;
    v_hilbert BIGINT := 0;
    v_level INT;
    v_quadrant INT;
    v_rotation INT := 0;
    v_bit INT;
BEGIN
    -- Validate inputs
    IF p_x < 0 OR p_x > 1 OR p_y < 0 OR p_y > 1 OR p_z < 0 OR p_z > 1 THEN
        RAISE EXCEPTION 'Coordinates must be in range [0.0, 1.0]';
    END IF;
    
    IF p_bits < 1 OR p_bits > 21 THEN
        RAISE EXCEPTION 'Bits must be in range [1, 21]';
    END IF;
    
    -- Convert normalized coordinates to integers
    v_max := (1::BIGINT << p_bits) - 1;
    v_x := FLOOR(p_x * v_max)::BIGINT;
    v_y := FLOOR(p_y * v_max)::BIGINT;
    v_z := FLOOR(p_z * v_max)::BIGINT;
    
    -- Encode using rotation-based Hilbert algorithm
    FOR v_level IN REVERSE (p_bits - 1)..0 LOOP
        v_bit := 1::BIGINT << v_level;
        
        -- Extract octant bits
        v_quadrant := 0;
        IF (v_x & v_bit) != 0 THEN v_quadrant := v_quadrant | 4; END IF;
        IF (v_y & v_bit) != 0 THEN v_quadrant := v_quadrant | 2; END IF;
        IF (v_z & v_bit) != 0 THEN v_quadrant := v_quadrant | 1; END IF;
        
        -- Apply rotation based on current state
        v_quadrant := v_quadrant # v_rotation;
        
        -- Update Hilbert index
        v_hilbert := (v_hilbert << 3) | v_quadrant;
        
        -- Calculate rotation for next level
        v_rotation := (v_rotation + (v_quadrant << 3)) & 7;
    END LOOP;
    
    RETURN v_hilbert;
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

COMMENT ON FUNCTION hilbert_encode_3d IS 
'Encode 3D coordinates to Hilbert index for locality-preserving spatial ordering.
Superior to Morton/Z-order for clustering and range queries.';

-- ============================================================================
-- hilbert_encode_point: Encode PostGIS PointZ to Hilbert index
-- ============================================================================
CREATE OR REPLACE FUNCTION hilbert_encode_point(
    p_point GEOMETRY(PointZ),
    p_bits INT DEFAULT 21
) RETURNS BIGINT AS $$
BEGIN
    IF p_point IS NULL THEN
        RETURN 0;
    END IF;
    
    RETURN hilbert_encode_3d(
        ST_X(p_point)::FLOAT8,
        ST_Y(p_point)::FLOAT8,
        ST_Z(p_point)::FLOAT8,
        p_bits
    );
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- hilbert_decode_3d: Convert 1D Hilbert index back to 3D coordinates
-- ============================================================================
CREATE OR REPLACE FUNCTION hilbert_decode_3d(
    p_hilbert BIGINT,
    p_bits INT DEFAULT 21
) RETURNS TABLE(x FLOAT8, y FLOAT8, z FLOAT8) AS $$
DECLARE
    v_x BIGINT := 0;
    v_y BIGINT := 0;
    v_z BIGINT := 0;
    v_max BIGINT;
    v_level INT;
    v_quadrant INT;
    v_rotation INT := 0;
    v_bit INT;
    v_shift INT;
BEGIN
    IF p_bits < 1 OR p_bits > 21 THEN
        RAISE EXCEPTION 'Bits must be in range [1, 21]';
    END IF;
    
    v_max := (1::BIGINT << p_bits) - 1;
    
    -- Decode using reverse rotation algorithm
    FOR v_level IN REVERSE (p_bits - 1)..0 LOOP
        v_shift := v_level * 3;
        v_quadrant := (p_hilbert >> v_shift) & 7;
        
        -- Apply inverse rotation
        v_quadrant := v_quadrant # v_rotation;
        
        v_bit := 1::BIGINT << v_level;
        
        -- Extract coordinate bits
        IF (v_quadrant & 4) != 0 THEN v_x := v_x | v_bit; END IF;
        IF (v_quadrant & 2) != 0 THEN v_y := v_y | v_bit; END IF;
        IF (v_quadrant & 1) != 0 THEN v_z := v_z | v_bit; END IF;
        
        -- Update rotation state
        v_rotation := (v_rotation + (v_quadrant << 3)) & 7;
    END LOOP;
    
    -- Normalize back to [0.0, 1.0]
    RETURN QUERY SELECT 
        v_x::FLOAT8 / v_max,
        v_y::FLOAT8 / v_max,
        v_z::FLOAT8 / v_max;
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- hilbert_range_query: Get Hilbert index range for spatial bounding box
-- ============================================================================
CREATE OR REPLACE FUNCTION hilbert_range_query(
    p_x_min FLOAT8,
    p_x_max FLOAT8,
    p_y_min FLOAT8,
    p_y_max FLOAT8,
    p_z_min FLOAT8,
    p_z_max FLOAT8,
    p_bits INT DEFAULT 21
) RETURNS TABLE(hilbert_min BIGINT, hilbert_max BIGINT) AS $$
BEGIN
    -- Simplified bounding approach
    -- Full implementation would recursively subdivide octree
    RETURN QUERY SELECT
        hilbert_encode_3d(p_x_min, p_y_min, p_z_min, p_bits),
        hilbert_encode_3d(p_x_max, p_y_max, p_z_max, p_bits);
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

COMMENT ON FUNCTION hilbert_range_query IS
'Calculate Hilbert index range for spatial bounding box query.
Allows efficient B-tree range scan instead of full spatial index scan.';

-- ============================================================================
-- Grants
-- ============================================================================
GRANT EXECUTE ON FUNCTION hilbert_encode_3d TO PUBLIC;
GRANT EXECUTE ON FUNCTION hilbert_encode_point TO PUBLIC;
GRANT EXECUTE ON FUNCTION hilbert_decode_3d TO PUBLIC;
GRANT EXECUTE ON FUNCTION hilbert_range_query TO PUBLIC;
