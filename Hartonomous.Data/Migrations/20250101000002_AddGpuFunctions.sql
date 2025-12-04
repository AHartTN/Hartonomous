-- Migration to create PL/Python GPU-accelerated functions
-- Requires plpython3u extension (must be run by superuser first)

-- Enable required extensions
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'plpython3u') THEN
        RAISE NOTICE 'plpython3u extension not found. Run as superuser: CREATE EXTENSION plpython3u;';
    END IF;
END $$;

-- Store the function directory path in a custom setting
-- Update this path to match your deployment
CREATE OR REPLACE FUNCTION get_python_function_path()
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
AS $$
BEGIN
    -- TODO: Update this path for your environment
    -- Windows: 'D:\\Repositories\\Hartonomous\\Hartonomous.Data\\Functions\\PlPython'
    -- Linux: '/var/lib/hartonomous/functions/plpython'
    RETURN '/var/lib/hartonomous/functions/plpython';
END;
$$;

-- ============================================================================
-- GPU Spatial K-Nearest Neighbors
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_spatial_knn(
    target_x DOUBLE PRECISION,
    target_y DOUBLE PRECISION,
    target_z DOUBLE PRECISION,
    k INTEGER,
    OUT constant_id UUID,
    OUT distance DOUBLE PRECISION
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
STABLE
AS $$
    import sys
    func_dir = plpy.execute("SELECT get_python_function_path() as path")[0]['path']
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from spatial_knn_gpu import spatial_knn_gpu
    
    query = """
        SELECT "Id" as id, 
               ST_X("Coordinate") as x, 
               ST_Y("Coordinate") as y, 
               ST_Z("Coordinate") as z
        FROM "Constants"
        WHERE "Status" = 2
          AND "Coordinate" IS NOT NULL
          AND "IsDeleted" = FALSE
    """
    
    result = plpy.execute(query)
    coordinates = [(row['id'], row['x'], row['y'], row['z']) for row in result]
    
    nearest = spatial_knn_gpu(target_x, target_y, target_z, k, coordinates)
    return nearest
$$;

CREATE INDEX IF NOT EXISTS idx_constants_status_coordinate 
ON "Constants" ("Status", "Coordinate") 
WHERE "IsDeleted" = FALSE;

-- ============================================================================
-- GPU Spatial Clustering (DBSCAN)
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_spatial_clustering(
    eps DOUBLE PRECISION DEFAULT 0.1,
    min_samples INTEGER DEFAULT 5,
    OUT constant_id UUID,
    OUT cluster_id INTEGER
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
STABLE
AS $$
    import sys
    func_dir = plpy.execute("SELECT get_python_function_path() as path")[0]['path']
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from spatial_clustering_gpu import spatial_clustering_gpu
    
    query = """
        SELECT "Id" as id,
               ST_X("Coordinate") as x,
               ST_Y("Coordinate") as y,
               ST_Z("Coordinate") as z
        FROM "Constants"
        WHERE "Status" = 2
          AND "Coordinate" IS NOT NULL
          AND "IsDeleted" = FALSE
    """
    
    result = plpy.execute(query)
    coordinates = [(row['id'], row['x'], row['y'], row['z']) for row in result]
    
    clusters = spatial_clustering_gpu(coordinates, eps, min_samples)
    return clusters
$$;

-- ============================================================================
-- GPU Cosine Similarity Search
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_similarity_search(
    target_hash VARCHAR(64),
    top_k INTEGER DEFAULT 10,
    OUT constant_id UUID,
    OUT similarity DOUBLE PRECISION
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
STABLE
AS $$
    import sys
    func_dir = plpy.execute("SELECT get_python_function_path() as path")[0]['path']
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from similarity_cosine_gpu import similarity_cosine_gpu
    
    target_query = """
        SELECT ST_X("Coordinate") as x,
               ST_Y("Coordinate") as y,
               ST_Z("Coordinate") as z
        FROM "Constants"
        WHERE "Hash" = $1
          AND "IsDeleted" = FALSE
        LIMIT 1
    """
    
    target_result = plpy.execute(plpy.prepare(target_query, ['varchar']), [target_hash])
    
    if len(target_result) == 0:
        return []
    
    target = target_result[0]
    target_embedding = [target['x'], target['y'], target['z']]
    
    query = """
        SELECT "Id" as id,
               ST_X("Coordinate") as x,
               ST_Y("Coordinate") as y,
               ST_Z("Coordinate") as z
        FROM "Constants"
        WHERE "Hash" != $1
          AND "Coordinate" IS NOT NULL
          AND "IsDeleted" = FALSE
    """
    
    result = plpy.execute(plpy.prepare(query, ['varchar']), [target_hash])
    embeddings = [(row['id'], [row['x'], row['y'], row['z']]) for row in result]
    
    similar = similarity_cosine_gpu(target_embedding, embeddings, top_k)
    return similar
$$;

CREATE INDEX IF NOT EXISTS idx_constants_hash_coordinate 
ON "Constants" ("Hash", "Coordinate") 
WHERE "IsDeleted" = FALSE;

-- ============================================================================
-- GPU BPE Learning
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_bpe_learn(
    max_vocab_size INTEGER DEFAULT 1000,
    min_frequency INTEGER DEFAULT 2,
    sample_size INTEGER DEFAULT 1000,
    OUT byte_pair INTEGER[],
    OUT frequency INTEGER
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
VOLATILE
AS $$
    import sys
    func_dir = plpy.execute("SELECT get_python_function_path() as path")[0]['path']
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from bpe_learning_gpu import bpe_learning_gpu
    
    query = """
        SELECT "OriginalData"
        FROM "ContentIngestions"
        WHERE "IsSuccessful" = TRUE
          AND "IsDeleted" = FALSE
          AND "OriginalData" IS NOT NULL
        ORDER BY "StartedAt" DESC
        LIMIT $1
    """
    
    result = plpy.execute(plpy.prepare(query, ['int']), [sample_size])
    
    byte_sequences = []
    for row in result:
        data = bytes(row['OriginalData'])
        if data:
            byte_sequences.append(list(data))
    
    if not byte_sequences:
        plpy.warning("No data available for BPE learning")
        return []
    
    merges = bpe_learning_gpu(byte_sequences, max_vocab_size, min_frequency)
    return merges
$$;

-- ============================================================================
-- GPU Hilbert Index Batch Computation
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_hilbert_index_batch(
    bits_per_dim INTEGER DEFAULT 21,
    batch_size INTEGER DEFAULT 10000,
    OUT constant_id UUID,
    OUT hilbert_index BIGINT
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
VOLATILE
AS $$
    import sys
    func_dir = plpy.execute("SELECT get_python_function_path() as path")[0]['path']
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from hilbert_indexing_gpu import hilbert_index_gpu
    
    query = """
        SELECT "Id" as id,
               ST_X("Coordinate") as x,
               ST_Y("Coordinate") as y,
               ST_Z("Coordinate") as z
        FROM "Constants"
        WHERE "Status" = 1
          AND "Coordinate" IS NOT NULL
          AND "HilbertIndex" IS NULL
          AND "IsDeleted" = FALSE
        LIMIT $1
    """
    
    result = plpy.execute(plpy.prepare(query, ['int']), [batch_size])
    coordinates = [(row['id'], row['x'], row['y'], row['z']) for row in result]
    
    if not coordinates:
        return []
    
    indices = hilbert_index_gpu(coordinates, bits_per_dim)
    return indices
$$;

CREATE INDEX IF NOT EXISTS idx_constants_status_hilbert 
ON "Constants" ("Status", "HilbertIndex") 
WHERE "IsDeleted" = FALSE AND "HilbertIndex" IS NULL;

-- ============================================================================
-- GPU Availability Check
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_check_availability(
    OUT has_cupy BOOLEAN,
    OUT has_cuml BOOLEAN,
    OUT gpu_count INTEGER,
    OUT gpu_memory_mb BIGINT,
    OUT error_message TEXT
)
RETURNS RECORD
LANGUAGE plpython3u
STABLE
AS $$
    result = {
        'has_cupy': False,
        'has_cuml': False,
        'gpu_count': 0,
        'gpu_memory_mb': 0,
        'error_message': None
    }
    
    try:
        import cupy as cp
        result['has_cupy'] = True
        result['gpu_count'] = cp.cuda.runtime.getDeviceCount()
        
        if result['gpu_count'] > 0:
            meminfo = cp.cuda.Device(0).mem_info
            result['gpu_memory_mb'] = meminfo[1] // (1024 * 1024)
    except Exception as e:
        result['error_message'] = f"CuPy error: {str(e)}"
    
    try:
        import cuml
        result['has_cuml'] = True
    except Exception as e:
        if result['error_message']:
            result['error_message'] += f"; cuML error: {str(e)}"
        else:
            result['error_message'] = f"cuML error: {str(e)}"
    
    return result
$$;

-- ============================================================================
-- Helper: Update Hilbert Indices from GPU Computation
-- ============================================================================

CREATE OR REPLACE FUNCTION update_hilbert_indices_gpu(
    batch_size INTEGER DEFAULT 10000
)
RETURNS TABLE(
    updated_count INTEGER,
    processing_time_ms BIGINT
)
LANGUAGE plpgsql
AS $$
DECLARE
    start_time TIMESTAMP;
    end_time TIMESTAMP;
    update_count INTEGER;
BEGIN
    start_time := clock_timestamp();
    
    -- Compute indices
    CREATE TEMP TABLE temp_hilbert_indices ON COMMIT DROP AS
    SELECT * FROM gpu_hilbert_index_batch(21, batch_size);
    
    -- Update Constants table
    UPDATE "Constants" c
    SET "HilbertIndex" = t.hilbert_index,
        "ModifiedAt" = CURRENT_TIMESTAMP
    FROM temp_hilbert_indices t
    WHERE c."Id" = t.constant_id;
    
    GET DIAGNOSTICS update_count = ROW_COUNT;
    
    end_time := clock_timestamp();
    
    RETURN QUERY SELECT 
        update_count,
        EXTRACT(MILLISECONDS FROM (end_time - start_time))::BIGINT;
END;
$$;

-- ============================================================================
-- Helper: Detect Spatial Landmarks from Clustering
-- ============================================================================

CREATE OR REPLACE FUNCTION detect_landmarks_from_clustering(
    eps DOUBLE PRECISION DEFAULT 0.1,
    min_samples INTEGER DEFAULT 5,
    min_cluster_size INTEGER DEFAULT 10
)
RETURNS TABLE(
    cluster_id INTEGER,
    centroid_x DOUBLE PRECISION,
    centroid_y DOUBLE PRECISION,
    centroid_z DOUBLE PRECISION,
    member_count INTEGER,
    suggested_name TEXT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH clustering AS (
        SELECT * FROM gpu_spatial_clustering(eps, min_samples)
    ),
    cluster_stats AS (
        SELECT 
            cl.cluster_id,
            AVG(ST_X(c."Coordinate")) as centroid_x,
            AVG(ST_Y(c."Coordinate")) as centroid_y,
            AVG(ST_Z(c."Coordinate")) as centroid_z,
            COUNT(*) as member_count
        FROM clustering cl
        JOIN "Constants" c ON c."Id" = cl.constant_id
        WHERE cl.cluster_id >= 0  -- Exclude noise points
        GROUP BY cl.cluster_id
        HAVING COUNT(*) >= min_cluster_size
    )
    SELECT 
        cs.cluster_id,
        cs.centroid_x,
        cs.centroid_y,
        cs.centroid_z,
        cs.member_count::INTEGER,
        'Cluster_' || cs.cluster_id || '_' || cs.member_count || '_points' as suggested_name
    FROM cluster_stats cs
    ORDER BY cs.member_count DESC;
END;
$$;

-- Grant execute permissions (adjust roles as needed)
-- GRANT EXECUTE ON FUNCTION gpu_spatial_knn TO app_user;
-- GRANT EXECUTE ON FUNCTION gpu_spatial_clustering TO app_user;
-- GRANT EXECUTE ON FUNCTION gpu_similarity_search TO app_user;
-- GRANT EXECUTE ON FUNCTION gpu_bpe_learn TO app_admin;
-- GRANT EXECUTE ON FUNCTION gpu_hilbert_index_batch TO app_admin;
-- GRANT EXECUTE ON FUNCTION gpu_check_availability TO app_user;
-- GRANT EXECUTE ON FUNCTION update_hilbert_indices_gpu TO app_admin;
-- GRANT EXECUTE ON FUNCTION detect_landmarks_from_clustering TO app_user;

COMMENT ON FUNCTION gpu_spatial_knn IS 'GPU-accelerated k-nearest neighbors search for 3D spatial coordinates';
COMMENT ON FUNCTION gpu_spatial_clustering IS 'GPU-accelerated DBSCAN clustering for spatial landmark detection';
COMMENT ON FUNCTION gpu_similarity_search IS 'GPU-accelerated cosine similarity search for content similarity';
COMMENT ON FUNCTION gpu_bpe_learn IS 'GPU-accelerated BPE vocabulary learning from content data';
COMMENT ON FUNCTION gpu_hilbert_index_batch IS 'GPU-accelerated batch Hilbert curve index computation';
COMMENT ON FUNCTION gpu_check_availability IS 'Check GPU availability and capabilities';
COMMENT ON FUNCTION update_hilbert_indices_gpu IS 'Batch update Hilbert indices using GPU computation';
COMMENT ON FUNCTION detect_landmarks_from_clustering IS 'Detect spatial landmarks using GPU clustering';
