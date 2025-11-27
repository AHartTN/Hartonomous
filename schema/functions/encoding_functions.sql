-- ============================================================================
-- Encoding Functions for Atom Values
-- ============================================================================
-- Implements various compression/encoding schemes for efficient storage
-- and progressive loading of atomic values
-- ============================================================================

-- ============================================================================
-- Encoding Type Constants (stored in M dimension)
-- ============================================================================
-- M = 0: Raw (unencoded)
-- M = 1: Sparse (threshold-based zero suppression)
-- M = 2: Delta (first-order differences)
-- M = 3: Run-Length Encoding (RLE)
-- M = 4: Delta + Sparse
-- M = 5: Delta + RLE
-- M = 6+: Hilbert LOD level (6=L0, 7=L1, etc.)
-- ============================================================================

-- ============================================================================
-- encode_sparse: Sparse encoding with configurable threshold
-- ============================================================================
-- Stores only values where |value| > threshold
-- Returns JSONB: {indices: [i1, i2, ...], values: [v1, v2, ...], len: N}
-- ============================================================================
CREATE OR REPLACE FUNCTION encode_sparse(
    p_values FLOAT8[],
    p_threshold FLOAT8 DEFAULT 1e-6
) RETURNS JSONB AS $$
DECLARE
    v_result JSONB;
    v_indices BIGINT[] := ARRAY[]::BIGINT[];
    v_values FLOAT8[] := ARRAY[]::FLOAT8[];
    v_idx INT;
    v_val FLOAT8;
BEGIN
    IF p_values IS NULL OR array_length(p_values, 1) IS NULL THEN
        RETURN jsonb_build_object(
            'indices', '[]'::JSONB,
            'values', '[]'::JSONB,
            'len', 0,
            'encoding', 'sparse'
        );
    END IF;
    
    -- Extract non-zero values
    FOR v_idx IN 1..array_length(p_values, 1) LOOP
        v_val := p_values[v_idx];
        IF ABS(v_val) > p_threshold THEN
            v_indices := array_append(v_indices, v_idx);
            v_values := array_append(v_values, v_val);
        END IF;
    END LOOP;
    
    RETURN jsonb_build_object(
        'indices', to_jsonb(v_indices),
        'values', to_jsonb(v_values),
        'len', array_length(p_values, 1),
        'encoding', 'sparse',
        'threshold', p_threshold,
        'sparsity', 1.0 - (array_length(v_indices, 1)::FLOAT8 / array_length(p_values, 1)::FLOAT8)
    );
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- decode_sparse: Reconstruct from sparse encoding
-- ============================================================================
CREATE OR REPLACE FUNCTION decode_sparse(
    p_encoded JSONB
) RETURNS FLOAT8[] AS $$
DECLARE
    v_result FLOAT8[];
    v_indices BIGINT[];
    v_values FLOAT8[];
    v_len INT;
    v_i INT;
    v_idx BIGINT;
BEGIN
    -- Extract metadata
    v_len := (p_encoded->>'len')::INT;
    v_indices := ARRAY(SELECT jsonb_array_elements_text(p_encoded->'indices')::BIGINT);
    v_values := ARRAY(SELECT jsonb_array_elements_text(p_encoded->'values')::FLOAT8);
    
    -- Initialize result array with zeros
    v_result := array_fill(0.0::FLOAT8, ARRAY[v_len]);
    
    -- Fill in non-zero values
    FOR v_i IN 1..array_length(v_indices, 1) LOOP
        v_idx := v_indices[v_i];
        v_result[v_idx] := v_values[v_i];
    END LOOP;
    
    RETURN v_result;
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- encode_delta: Delta encoding (first-order differences)
-- ============================================================================
CREATE OR REPLACE FUNCTION encode_delta(
    p_values FLOAT8[]
) RETURNS JSONB AS $$
DECLARE
    v_deltas FLOAT8[] := ARRAY[]::FLOAT8[];
    v_idx INT;
    v_prev FLOAT8;
BEGIN
    IF p_values IS NULL OR array_length(p_values, 1) IS NULL THEN
        RETURN jsonb_build_object(
            'base', 0.0,
            'deltas', '[]'::JSONB,
            'encoding', 'delta'
        );
    END IF;
    
    -- Store first value as base
    v_prev := p_values[1];
    
    -- Calculate deltas
    FOR v_idx IN 2..array_length(p_values, 1) LOOP
        v_deltas := array_append(v_deltas, p_values[v_idx] - v_prev);
        v_prev := p_values[v_idx];
    END LOOP;
    
    RETURN jsonb_build_object(
        'base', p_values[1],
        'deltas', to_jsonb(v_deltas),
        'encoding', 'delta'
    );
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- decode_delta: Reconstruct from delta encoding
-- ============================================================================
CREATE OR REPLACE FUNCTION decode_delta(
    p_encoded JSONB
) RETURNS FLOAT8[] AS $$
DECLARE
    v_result FLOAT8[];
    v_deltas FLOAT8[];
    v_base FLOAT8;
    v_current FLOAT8;
    v_i INT;
BEGIN
    v_base := (p_encoded->>'base')::FLOAT8;
    v_deltas := ARRAY(SELECT jsonb_array_elements_text(p_encoded->'deltas')::FLOAT8);
    
    v_result := ARRAY[v_base];
    v_current := v_base;
    
    FOR v_i IN 1..array_length(v_deltas, 1) LOOP
        v_current := v_current + v_deltas[v_i];
        v_result := array_append(v_result, v_current);
    END LOOP;
    
    RETURN v_result;
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- encode_rle: Run-Length Encoding
-- ============================================================================
CREATE OR REPLACE FUNCTION encode_rle(
    p_values FLOAT8[],
    p_epsilon FLOAT8 DEFAULT 1e-9
) RETURNS JSONB AS $$
DECLARE
    v_runs JSONB[] := ARRAY[]::JSONB[];
    v_current FLOAT8;
    v_count INT := 0;
    v_idx INT;
    v_val FLOAT8;
BEGIN
    IF p_values IS NULL OR array_length(p_values, 1) IS NULL THEN
        RETURN jsonb_build_object(
            'runs', '[]'::JSONB,
            'encoding', 'rle'
        );
    END IF;
    
    v_current := p_values[1];
    v_count := 1;
    
    FOR v_idx IN 2..array_length(p_values, 1) LOOP
        v_val := p_values[v_idx];
        
        -- Check if value is "equal" within epsilon
        IF ABS(v_val - v_current) <= p_epsilon THEN
            v_count := v_count + 1;
        ELSE
            -- Store run
            v_runs := array_append(v_runs, 
                jsonb_build_object('value', v_current, 'count', v_count)
            );
            v_current := v_val;
            v_count := 1;
        END IF;
    END LOOP;
    
    -- Store final run
    v_runs := array_append(v_runs, 
        jsonb_build_object('value', v_current, 'count', v_count)
    );
    
    RETURN jsonb_build_object(
        'runs', to_jsonb(v_runs),
        'encoding', 'rle',
        'compression_ratio', array_length(p_values, 1)::FLOAT8 / array_length(v_runs, 1)::FLOAT8
    );
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- decode_rle: Reconstruct from RLE encoding
-- ============================================================================
CREATE OR REPLACE FUNCTION decode_rle(
    p_encoded JSONB
) RETURNS FLOAT8[] AS $$
DECLARE
    v_result FLOAT8[] := ARRAY[]::FLOAT8[];
    v_run JSONB;
    v_value FLOAT8;
    v_count INT;
    v_i INT;
BEGIN
    FOR v_run IN SELECT jsonb_array_elements(p_encoded->'runs') LOOP
        v_value := (v_run->>'value')::FLOAT8;
        v_count := (v_run->>'count')::INT;
        
        FOR v_i IN 1..v_count LOOP
            v_result := array_append(v_result, v_value);
        END LOOP;
    END LOOP;
    
    RETURN v_result;
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- encode_auto: Automatically select best encoding
-- ============================================================================
CREATE OR REPLACE FUNCTION encode_auto(
    p_values FLOAT8[],
    OUT encoding_type SMALLINT,
    OUT encoded_data JSONB,
    OUT compression_ratio FLOAT8
) AS $$
DECLARE
    v_raw_size INT;
    v_sparse JSONB;
    v_delta JSONB;
    v_rle JSONB;
    v_sparse_size INT;
    v_delta_size INT;
    v_rle_size INT;
    v_best_size INT;
BEGIN
    IF p_values IS NULL OR array_length(p_values, 1) IS NULL THEN
        encoding_type := 0;
        encoded_data := NULL;
        compression_ratio := 1.0;
        RETURN;
    END IF;
    
    v_raw_size := array_length(p_values, 1) * 8; -- 8 bytes per float64
    
    -- Try sparse encoding
    v_sparse := encode_sparse(p_values);
    v_sparse_size := (jsonb_array_length(v_sparse->'indices') * 12)::INT; -- index + value
    
    -- Try delta encoding
    v_delta := encode_delta(p_values);
    v_delta_size := (8 + jsonb_array_length(v_delta->'deltas') * 8)::INT;
    
    -- Try RLE encoding
    v_rle := encode_rle(p_values);
    v_rle_size := (jsonb_array_length(v_rle->'runs') * 12)::INT;
    
    -- Select best encoding
    v_best_size := LEAST(v_raw_size, v_sparse_size, v_delta_size, v_rle_size);
    
    IF v_best_size = v_sparse_size AND v_sparse_size < v_raw_size THEN
        encoding_type := 1;
        encoded_data := v_sparse;
        compression_ratio := v_raw_size::FLOAT8 / v_sparse_size::FLOAT8;
    ELSIF v_best_size = v_delta_size AND v_delta_size < v_raw_size THEN
        encoding_type := 2;
        encoded_data := v_delta;
        compression_ratio := v_raw_size::FLOAT8 / v_delta_size::FLOAT8;
    ELSIF v_best_size = v_rle_size AND v_rle_size < v_raw_size THEN
        encoding_type := 3;
        encoded_data := v_rle;
        compression_ratio := v_raw_size::FLOAT8 / v_rle_size::FLOAT8;
    ELSE
        -- No compression benefit, use raw
        encoding_type := 0;
        encoded_data := to_jsonb(p_values);
        compression_ratio := 1.0;
    END IF;
END;
$$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;

-- ============================================================================
-- Grants
-- ============================================================================
GRANT EXECUTE ON FUNCTION encode_sparse TO PUBLIC;
GRANT EXECUTE ON FUNCTION decode_sparse TO PUBLIC;
GRANT EXECUTE ON FUNCTION encode_delta TO PUBLIC;
GRANT EXECUTE ON FUNCTION decode_delta TO PUBLIC;
GRANT EXECUTE ON FUNCTION encode_rle TO PUBLIC;
GRANT EXECUTE ON FUNCTION decode_rle TO PUBLIC;
GRANT EXECUTE ON FUNCTION encode_auto TO PUBLIC;
