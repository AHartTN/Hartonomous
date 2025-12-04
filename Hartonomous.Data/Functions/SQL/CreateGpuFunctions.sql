-- SQL wrapper functions for PL/Python GPU-accelerated operations
-- Requires PostgreSQL with plpython3u extension

-- Enable PL/Python extension (must be run by superuser)
-- CREATE EXTENSION IF NOT EXISTS plpython3u;

-- ============================================================================
-- Spatial K-Nearest Neighbors (GPU-accelerated)
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
AS $$
    # Load Python function
    import sys
    import os
    
    # Add function directory to Python path
    func_dir = '/path/to/Hartonomous.Data/Functions/PlPython'
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from spatial_knn_gpu import spatial_knn_gpu
    
    # Query all active constants with coordinates
    query = """
        SELECT id, 
               ST_X(coordinate) as x, 
               ST_Y(coordinate) as y, 
               ST_Z(coordinate) as z
        FROM "Constants"
        WHERE "Status" = 2  -- Active status
          AND coordinate IS NOT NULL
          AND "IsDeleted" = FALSE
    """
    
    result = plpy.execute(query)
    
    # Convert to format expected by Python function
    coordinates = [(row['id'], row['x'], row['y'], row['z']) for row in result]
    
    # Call GPU function
    nearest = spatial_knn_gpu(target_x, target_y, target_z, k, coordinates)
    
    # Return results
    return nearest
$$;

COMMENT ON FUNCTION gpu_spatial_knn IS 'GPU-accelerated k-nearest neighbors search for 3D spatial coordinates';

-- ============================================================================
-- Spatial Clustering (GPU-accelerated DBSCAN)
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_spatial_clustering(
    eps DOUBLE PRECISION DEFAULT 0.1,
    min_samples INTEGER DEFAULT 5,
    OUT constant_id UUID,
    OUT cluster_id INTEGER
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
AS $$
    import sys
    import os
    
    func_dir = '/path/to/Hartonomous.Data/Functions/PlPython'
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from spatial_clustering_gpu import spatial_clustering_gpu
    
    # Query all active constants
    query = """
        SELECT id,
               ST_X(coordinate) as x,
               ST_Y(coordinate) as y,
               ST_Z(coordinate) as z
        FROM "Constants"
        WHERE "Status" = 2
          AND coordinate IS NOT NULL
          AND "IsDeleted" = FALSE
    """
    
    result = plpy.execute(query)
    coordinates = [(row['id'], row['x'], row['y'], row['z']) for row in result]
    
    # Perform clustering
    clusters = spatial_clustering_gpu(coordinates, eps, min_samples)
    
    return clusters
$$;

COMMENT ON FUNCTION gpu_spatial_clustering IS 'GPU-accelerated DBSCAN clustering for spatial landmark detection';

-- ============================================================================
-- Cosine Similarity (GPU-accelerated)
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_similarity_search(
    target_hash VARCHAR(64),
    top_k INTEGER DEFAULT 10,
    OUT constant_id UUID,
    OUT similarity DOUBLE PRECISION
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
AS $$
    import sys
    import os
    
    func_dir = '/path/to/Hartonomous.Data/Functions/PlPython'
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from similarity_cosine_gpu import similarity_cosine_gpu
    
    # Get target constant's coordinate as embedding
    target_query = plpy.prepare("""
        SELECT ST_X(coordinate) as x,
               ST_Y(coordinate) as y,
               ST_Z(coordinate) as z
        FROM "Constants"
        WHERE "Hash" = $1
          AND "IsDeleted" = FALSE
    """, ['varchar'])
    
    target_result = plpy.execute(target_query, [target_hash])
    
    if len(target_result) == 0:
        return []
    
    target = target_result[0]
    target_embedding = [target['x'], target['y'], target['z']]
    
    # Get all other constants' embeddings
    query = """
        SELECT id,
               ST_X(coordinate) as x,
               ST_Y(coordinate) as y,
               ST_Z(coordinate) as z
        FROM "Constants"
        WHERE "Hash" != $1
          AND coordinate IS NOT NULL
          AND "IsDeleted" = FALSE
    """
    
    result = plpy.execute(query, [target_hash])
    embeddings = [(row['id'], [row['x'], row['y'], row['z']]) for row in result]
    
    # Compute similarities
    similar = similarity_cosine_gpu(target_embedding, embeddings, top_k)
    
    return similar
$$;

COMMENT ON FUNCTION gpu_similarity_search IS 'GPU-accelerated cosine similarity search for content similarity detection';

-- ============================================================================
-- BPE Learning (GPU-accelerated)
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_bpe_learn(
    max_vocab_size INTEGER DEFAULT 1000,
    min_frequency INTEGER DEFAULT 2,
    OUT byte_pair INTEGER[],
    OUT frequency INTEGER
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
AS $$
    import sys
    import os
    
    func_dir = '/path/to/Hartonomous.Data/Functions/PlPython'
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from bpe_learning_gpu import bpe_learning_gpu
    
    # Get recent content ingestions to learn from
    query = """
        SELECT "OriginalData"
        FROM "ContentIngestions"
        WHERE "IsSuccessful" = TRUE
          AND "IsDeleted" = FALSE
        ORDER BY "StartedAt" DESC
        LIMIT 1000
    """
    
    result = plpy.execute(query)
    
    # Convert content to byte sequences
    byte_sequences = []
    for row in result:
        data = row['OriginalData']
        if data:
            byte_sequences.append(list(data))
    
    if not byte_sequences:
        return []
    
    # Learn BPE vocabulary
    merges = bpe_learning_gpu(byte_sequences, max_vocab_size, min_frequency)
    
    return merges
$$;

COMMENT ON FUNCTION gpu_bpe_learn IS 'GPU-accelerated BPE vocabulary learning from content data';

-- ============================================================================
-- Hilbert Index Computation (GPU-accelerated)
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_hilbert_index_batch(
    bits_per_dim INTEGER DEFAULT 21,
    OUT constant_id UUID,
    OUT hilbert_index BIGINT
)
RETURNS SETOF RECORD
LANGUAGE plpython3u
AS $$
    import sys
    import os
    
    func_dir = '/path/to/Hartonomous.Data/Functions/PlPython'
    if func_dir not in sys.path:
        sys.path.insert(0, func_dir)
    
    from hilbert_indexing_gpu import hilbert_index_gpu
    
    # Get all constants that need indexing
    query = """
        SELECT id,
               ST_X(coordinate) as x,
               ST_Y(coordinate) as y,
               ST_Z(coordinate) as z
        FROM "Constants"
        WHERE "Status" = 1  -- Projected status
          AND coordinate IS NOT NULL
          AND "HilbertIndex" IS NULL
          AND "IsDeleted" = FALSE
        LIMIT 10000
    """
    
    result = plpy.execute(query)
    coordinates = [(row['id'], row['x'], row['y'], row['z']) for row in result]
    
    if not coordinates:
        return []
    
    # Compute Hilbert indices
    indices = hilbert_index_gpu(coordinates, bits_per_dim)
    
    return indices
$$;

COMMENT ON FUNCTION gpu_hilbert_index_batch IS 'GPU-accelerated batch Hilbert curve index computation for spatial locality';

-- ============================================================================
-- Helper function to check GPU availability
-- ============================================================================

CREATE OR REPLACE FUNCTION gpu_check_availability()
RETURNS TABLE(
    has_cupy BOOLEAN,
    has_cuml BOOLEAN,
    gpu_count INTEGER,
    gpu_memory_mb BIGINT
)
LANGUAGE plpython3u
AS $$
    result = {
        'has_cupy': False,
        'has_cuml': False,
        'gpu_count': 0,
        'gpu_memory_mb': 0
    }
    
    try:
        import cupy as cp
        result['has_cupy'] = True
        result['gpu_count'] = cp.cuda.runtime.getDeviceCount()
        
        if result['gpu_count'] > 0:
            meminfo = cp.cuda.Device(0).mem_info
            result['gpu_memory_mb'] = meminfo[1] // (1024 * 1024)  # Total memory in MB
    except:
        pass
    
    try:
        import cuml
        result['has_cuml'] = True
    except:
        pass
    
    return [result]
$$;

COMMENT ON FUNCTION gpu_check_availability IS 'Check GPU availability and capabilities for accelerated functions';
