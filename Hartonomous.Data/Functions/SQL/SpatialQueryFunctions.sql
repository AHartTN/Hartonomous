-- ============================================================================
-- Hartonomous Spatial Query Functions
-- PostgreSQL stored procedures for optimized spatial queries
-- ============================================================================
-- These functions are applied via EF Core migrations but preserved here
-- for reference and manual application if needed.
-- ============================================================================

-- Function 1: get_nearby_constants - Spatial radius query
CREATE OR REPLACE FUNCTION get_nearby_constants(
    p_y INT,
    p_z INT,
    p_m INT,
    p_radius FLOAT,
    p_max_results INT DEFAULT 100
)
RETURNS TABLE (
    id UUID,
    hash VARCHAR(64),
    data BYTEA,
    size INT,
    status INT,
    reference_count BIGINT,
    frequency BIGINT,
    last_accessed_at TIMESTAMPTZ,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256)
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.status,
        c.reference_count,
        c.frequency,
        c.last_accessed_at,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by
    FROM "Constants" c
    WHERE c.hilbert_high IS NOT NULL
      AND c.status = 2
      AND c.is_deleted = FALSE
      AND sqrt(
          power(CAST(c.quantized_entropy AS FLOAT) - p_y, 2) +
          power(CAST(c.quantized_compressibility AS FLOAT) - p_z, 2) +
          power(CAST(c.quantized_connectivity AS FLOAT) - p_m, 2)
      ) <= p_radius
    ORDER BY sqrt(
          power(CAST(c.quantized_entropy AS FLOAT) - p_y, 2) +
          power(CAST(c.quantized_compressibility AS FLOAT) - p_z, 2) +
          power(CAST(c.quantized_connectivity AS FLOAT) - p_m, 2)
      )
    LIMIT p_max_results;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 2: get_k_nearest_constants - k-NN query
CREATE OR REPLACE FUNCTION get_k_nearest_constants(
    p_y INT,
    p_z INT,
    p_m INT,
    p_k INT DEFAULT 10
)
RETURNS TABLE (
    id UUID,
    hash VARCHAR(64),
    data BYTEA,
    size INT,
    status INT,
    reference_count BIGINT,
    frequency BIGINT,
    last_accessed_at TIMESTAMPTZ,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256),
    distance FLOAT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.status,
        c.reference_count,
        c.frequency,
        c.last_accessed_at,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by,
        sqrt(
            power(CAST(c.quantized_entropy AS FLOAT) - p_y, 2) +
            power(CAST(c.quantized_compressibility AS FLOAT) - p_z, 2) +
            power(CAST(c.quantized_connectivity AS FLOAT) - p_m, 2)
        ) as distance
    FROM "Constants" c
    WHERE c.hilbert_high IS NOT NULL
      AND c.status = 2
      AND c.is_deleted = FALSE
    ORDER BY distance
    LIMIT p_k;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 3: get_by_hilbert_range - Hilbert B-tree range query
CREATE OR REPLACE FUNCTION get_by_hilbert_range(
    p_start_high BIGINT,
    p_start_low BIGINT,
    p_end_high BIGINT,
    p_end_low BIGINT,
    p_max_results INT DEFAULT 100
)
RETURNS TABLE (
    id UUID,
    hash VARCHAR(64),
    data BYTEA,
    size INT,
    status INT,
    reference_count BIGINT,
    frequency BIGINT,
    last_accessed_at TIMESTAMPTZ,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256)
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.status,
        c.reference_count,
        c.frequency,
        c.last_accessed_at,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by
    FROM "Constants" c
    WHERE c.hilbert_high IS NOT NULL
      AND c.is_deleted = FALSE
      AND (c.hilbert_high > p_start_high OR 
           (c.hilbert_high = p_start_high AND c.hilbert_low >= p_start_low))
      AND (c.hilbert_high < p_end_high OR
           (c.hilbert_high = p_end_high AND c.hilbert_low <= p_end_low))
    ORDER BY c.hilbert_high, c.hilbert_low
    LIMIT p_max_results;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 4: get_top_by_frequency - Frequency-based query
CREATE OR REPLACE FUNCTION get_top_by_frequency(
    p_count INT DEFAULT 100
)
RETURNS TABLE (
    id UUID,
    hash VARCHAR(64),
    data BYTEA,
    size INT,
    status INT,
    reference_count BIGINT,
    frequency BIGINT,
    last_accessed_at TIMESTAMPTZ,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256)
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.status,
        c.reference_count,
        c.frequency,
        c.last_accessed_at,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by
    FROM "Constants" c
    WHERE c.is_deleted = FALSE
    ORDER BY c.frequency DESC
    LIMIT p_count;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 5: get_stale_constants - Temporal query
CREATE OR REPLACE FUNCTION get_stale_constants(
    p_older_than TIMESTAMPTZ,
    p_max_results INT DEFAULT 1000
)
RETURNS TABLE (
    id UUID,
    hash VARCHAR(64),
    data BYTEA,
    size INT,
    status INT,
    reference_count BIGINT,
    frequency BIGINT,
    last_accessed_at TIMESTAMPTZ,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256)
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.status,
        c.reference_count,
        c.frequency,
        c.last_accessed_at,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by
    FROM "Constants" c
    WHERE c.is_deleted = FALSE
      AND c.last_accessed_at < p_older_than
      AND c.reference_count = 0
    ORDER BY c.last_accessed_at
    LIMIT p_max_results;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 6: get_deduplication_stats - Analytics aggregation
CREATE OR REPLACE FUNCTION get_deduplication_stats()
RETURNS TABLE (
    total_constants INT,
    unique_constants INT,
    deduplication_ratio FLOAT
) AS $$
DECLARE
    v_total INT;
    v_unique INT;
BEGIN
    SELECT COUNT(*) INTO v_total FROM "Constants" WHERE is_deleted = FALSE;
    SELECT COUNT(*) INTO v_unique FROM "Constants" WHERE is_deleted = FALSE AND status != 3;
    
    RETURN QUERY
    SELECT 
        v_total,
        v_unique,
        CASE WHEN v_total > 0 THEN CAST(v_unique AS FLOAT) / v_total ELSE 0.0 END;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 7: get_total_storage_size - Storage aggregation
CREATE OR REPLACE FUNCTION get_total_storage_size()
RETURNS BIGINT AS $$
DECLARE
    v_total_size BIGINT;
BEGIN
    SELECT COALESCE(SUM(size), 0) 
    INTO v_total_size
    FROM "Constants"
    WHERE is_deleted = FALSE AND status != 3;
    
    RETURN v_total_size;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 8: get_nearby_landmarks - Spatial radius query for landmarks
CREATE OR REPLACE FUNCTION get_nearby_landmarks(
    p_hilbert_high BIGINT,
    p_hilbert_low BIGINT,
    p_max_distance FLOAT,
    p_precision INT DEFAULT 42,
    p_max_results INT DEFAULT 100
)
RETURNS TABLE (
    id UUID,
    name VARCHAR(255),
    description TEXT,
    hilbert_prefix_high BIGINT,
    hilbert_prefix_low BIGINT,
    level INT,
    constant_count BIGINT,
    density FLOAT,
    is_active BOOLEAN,
    last_statistics_update TIMESTAMPTZ,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256),
    distance FLOAT
) AS $$
DECLARE
    v_min_high BIGINT;
    v_min_low BIGINT;
    v_max_high BIGINT;
    v_max_low BIGINT;
BEGIN
    -- Get Hilbert range for radius (approximation via bit shifting)
    -- Simple approach: shift down by max_distance factor
    v_min_high := GREATEST(p_hilbert_high - CAST(p_max_distance AS BIGINT), 0);
    v_min_low := GREATEST(p_hilbert_low - CAST(p_max_distance AS BIGINT), 0);
    v_max_high := p_hilbert_high + CAST(p_max_distance AS BIGINT);
    v_max_low := p_hilbert_low + CAST(p_max_distance AS BIGINT);

    RETURN QUERY
    SELECT 
        l.id,
        l.name,
        l.description,
        l.hilbert_prefix_high,
        l.hilbert_prefix_low,
        l.level,
        l.constant_count,
        l.density,
        l.is_active,
        l.last_statistics_update,
        l.created_at,
        l.created_by,
        l.updated_at,
        l.updated_by,
        l.is_deleted,
        l.deleted_at,
        l.deleted_by,
        -- Hilbert distance approximation (abs difference in both parts)
        CAST(ABS(l.hilbert_prefix_high - p_hilbert_high) + ABS(l.hilbert_prefix_low - p_hilbert_low) AS FLOAT) as distance
    FROM landmarks l
    WHERE l.is_active = TRUE
      AND l.is_deleted = FALSE
      AND ((l.hilbert_prefix_high > v_min_high) OR (l.hilbert_prefix_high = v_min_high AND l.hilbert_prefix_low >= v_min_low))
      AND ((l.hilbert_prefix_high < v_max_high) OR (l.hilbert_prefix_high = v_max_high AND l.hilbert_prefix_low <= v_max_low))
    ORDER BY distance
    LIMIT p_max_results;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 9: get_nearest_landmark - Single nearest landmark
CREATE OR REPLACE FUNCTION get_nearest_landmark(
    p_hilbert_high BIGINT,
    p_hilbert_low BIGINT
)
RETURNS TABLE (
    id UUID,
    name VARCHAR(255),
    description TEXT,
    hilbert_prefix_high BIGINT,
    hilbert_prefix_low BIGINT,
    level INT,
    constant_count BIGINT,
    density FLOAT,
    is_active BOOLEAN,
    last_statistics_update TIMESTAMPTZ,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256)
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        l.id,
        l.name,
        l.description,
        l.hilbert_prefix_high,
        l.hilbert_prefix_low,
        l.level,
        l.constant_count,
        l.density,
        l.is_active,
        l.last_statistics_update,
        l.created_at,
        l.created_by,
        l.updated_at,
        l.updated_by,
        l.is_deleted,
        l.deleted_at,
        l.deleted_by
    FROM landmarks l
    WHERE l.is_active = TRUE
      AND l.is_deleted = FALSE
    ORDER BY 
        ABS(l.hilbert_prefix_high - p_hilbert_high) + ABS(l.hilbert_prefix_low - p_hilbert_low)
    LIMIT 1;
END;
$$ LANGUAGE plpgsql STABLE;
