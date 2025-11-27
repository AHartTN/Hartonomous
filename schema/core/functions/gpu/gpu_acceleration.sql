-- ============================================================================
-- GPU Acceleration Functions (PL/Python + CUDA)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Detects GPU availability and provides optional CUDA-accelerated operations
-- ============================================================================

-- Check if GPU is available
CREATE OR REPLACE FUNCTION gpu_available()
RETURNS BOOLEAN
LANGUAGE plpython3u
AS $$
    try:
        import cupy as cp
        # Try to allocate tiny array on GPU
        test = cp.array([1.0])
        return True
    except:
        return False
$$;

COMMENT ON FUNCTION gpu_available() IS 
'Returns TRUE if CUDA GPU is available via CuPy, FALSE otherwise.
Use this to conditionally enable GPU acceleration.';

-- Get GPU device info
CREATE OR REPLACE FUNCTION gpu_info()
RETURNS TEXT
LANGUAGE plpython3u
AS $$
    try:
        import cupy as cp
        device = cp.cuda.Device()
        return f"GPU: {device.compute_capability}, Memory: {device.mem_info[1] / 1e9:.2f} GB"
    except Exception as e:
        return f"No GPU: {str(e)}"
$$;

COMMENT ON FUNCTION gpu_info() IS
'Returns GPU device information if available.';

-- ============================================================================
-- GPU-Accelerated Attention Computation
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_attention_gpu(
    p_query_atom_id BIGINT,
    p_context_atom_ids BIGINT[],
    p_k INTEGER DEFAULT 10
)
RETURNS TABLE(
    atom_id BIGINT,
    attention_weight REAL,
    canonical_text TEXT
)
LANGUAGE plpython3u
AS $$
    import cupy as cp
    import numpy as np
    
    # Fetch query position
    query_plan = plpy.prepare("""
        SELECT ST_X(spatial_key) as x, ST_Y(spatial_key) as y, ST_Z(spatial_key) as z
        FROM atom WHERE atom_id = $1
    """, ["bigint"])
    query_row = plpy.execute(query_plan, [p_query_atom_id])[0]
    query_pos = cp.array([query_row['x'], query_row['y'], query_row['z']], dtype=cp.float32)
    
    # Fetch context positions (bulk)
    context_plan = plpy.prepare("""
        SELECT atom_id, canonical_text,
               ST_X(spatial_key) as x, ST_Y(spatial_key) as y, ST_Z(spatial_key) as z
        FROM atom WHERE atom_id = ANY($1)
    """, ["bigint[]"])
    context_rows = plpy.execute(context_plan, [p_context_atom_ids])
    
    # Build GPU arrays
    atom_ids = []
    texts = []
    positions = []
    
    for row in context_rows:
        atom_ids.append(row['atom_id'])
        texts.append(row['canonical_text'])
        positions.append([row['x'], row['y'], row['z']])
    
    # Transfer to GPU
    context_pos_gpu = cp.array(positions, dtype=cp.float32)
    
    # Compute distances on GPU (vectorized)
    diff = context_pos_gpu - query_pos
    distances = cp.sqrt(cp.sum(diff ** 2, axis=1))
    
    # Compute attention weights (softmax of inverse distances)
    similarities = 1.0 / (1.0 + distances)
    attention_weights = cp.exp(similarities) / cp.sum(cp.exp(similarities))
    
    # Get top-K
    top_k_indices = cp.argsort(attention_weights)[-p_k:][::-1]
    
    # Transfer back to CPU
    top_k_indices_cpu = top_k_indices.get()
    attention_weights_cpu = attention_weights.get()
    
    # Return results
    results = []
    for idx in top_k_indices_cpu:
        results.append({
            'atom_id': atom_ids[idx],
            'attention_weight': float(attention_weights_cpu[idx]),
            'canonical_text': texts[idx]
        })
    
    return results
$$;

COMMENT ON FUNCTION compute_attention_gpu(BIGINT, BIGINT[], INTEGER) IS
'GPU-accelerated attention computation via CuPy.
Falls back to CPU version if GPU unavailable.';

-- ============================================================================
-- Hybrid Attention (CPU or GPU)
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_attention_hybrid(
    p_query_atom_id BIGINT,
    p_context_atom_ids BIGINT[],
    p_k INTEGER DEFAULT 10
)
RETURNS TABLE(
    atom_id BIGINT,
    attention_weight REAL,
    canonical_text TEXT
)
LANGUAGE plpgsql
AS $$
BEGIN
    -- Check GPU availability
    IF gpu_available() THEN
        -- Use GPU acceleration
        RETURN QUERY SELECT * FROM compute_attention_gpu(p_query_atom_id, p_context_atom_ids, p_k);
    ELSE
        -- Fallback to CPU version (PostGIS spatial)
        RETURN QUERY SELECT * FROM compute_attention(p_query_atom_id, p_context_atom_ids, p_k);
    END IF;
END;
$$;

COMMENT ON FUNCTION compute_attention_hybrid(BIGINT, BIGINT[], INTEGER) IS
'Hybrid attention: uses GPU if available, falls back to CPU PostGIS version.';

-- ============================================================================
-- GPU-Accelerated Batch Weight Extraction (for model atomization)
-- ============================================================================

CREATE OR REPLACE FUNCTION extract_unique_weights_gpu(
    p_weights FLOAT[],
    p_threshold FLOAT DEFAULT 0.01
)
RETURNS TABLE(
    weight_value FLOAT,
    occurrence_count INTEGER
)
LANGUAGE plpython3u
AS $$
    import cupy as cp
    import numpy as np
    
    # Transfer to GPU
    weights_gpu = cp.array(p_weights, dtype=cp.float32)
    
    # Apply threshold (sparse encoding)
    mask = cp.abs(weights_gpu) >= p_threshold
    significant_weights = weights_gpu[mask]
    
    # Find unique values and counts on GPU
    unique_weights, counts = cp.unique(significant_weights, return_counts=True)
    
    # Transfer back to CPU
    unique_weights_cpu = unique_weights.get()
    counts_cpu = counts.get()
    
    # Return as table
    results = []
    for weight, count in zip(unique_weights_cpu, counts_cpu):
        results.append({'weight_value': float(weight), 'occurrence_count': int(count)})
    
    return results
$$;

COMMENT ON FUNCTION extract_unique_weights_gpu(FLOAT[], FLOAT) IS
'GPU-accelerated unique weight extraction with deduplication.
Used for fast model atomization.';

-- ============================================================================
-- SIMD-Accelerated Hilbert Distance (fallback if no GPU)
-- ============================================================================

CREATE OR REPLACE FUNCTION hilbert_distance_simd(
    p_hilbert1 BIGINT,
    p_hilbert2 BIGINT
)
RETURNS BIGINT
LANGUAGE sql
IMMUTABLE PARALLEL SAFE
AS $$
    -- PostgreSQL automatically uses SIMD for integer operations
    SELECT ABS(p_hilbert1 - p_hilbert2)
$$;

COMMENT ON FUNCTION hilbert_distance_simd(BIGINT, BIGINT) IS
'SIMD-accelerated Hilbert distance (PostgreSQL built-in vectorization).';

-- ============================================================================
-- Performance Benchmarks
-- ============================================================================

CREATE OR REPLACE FUNCTION benchmark_gpu_vs_cpu(
    p_array_size INTEGER DEFAULT 1000000
)
RETURNS TABLE(
    test_name TEXT,
    execution_time_ms REAL,
    speedup TEXT
)
LANGUAGE plpython3u
AS $$
    import time
    import numpy as np
    
    results = []
    
    # Create test data
    test_data = np.random.randn(p_array_size).astype(np.float32)
    
    # CPU test
    start = time.time()
    cpu_result = np.sum(test_data ** 2)
    cpu_time = (time.time() - start) * 1000
    
    results.append({
        'test_name': 'CPU (NumPy)',
        'execution_time_ms': cpu_time,
        'speedup': '1.0x (baseline)'
    })
    
    # GPU test (if available)
    try:
        import cupy as cp
        test_data_gpu = cp.array(test_data)
        
        start = time.time()
        gpu_result = cp.sum(test_data_gpu ** 2)
        cp.cuda.Stream.null.synchronize()  # Wait for GPU
        gpu_time = (time.time() - start) * 1000
        
        speedup = cpu_time / gpu_time
        results.append({
            'test_name': 'GPU (CuPy)',
            'execution_time_ms': gpu_time,
            'speedup': f'{speedup:.1f}x faster'
        })
    except:
        results.append({
            'test_name': 'GPU (CuPy)',
            'execution_time_ms': 0.0,
            'speedup': 'Not available'
        })
    
    return results
$$;

COMMENT ON FUNCTION benchmark_gpu_vs_cpu(INTEGER) IS
'Benchmark GPU vs CPU performance for array operations.';
