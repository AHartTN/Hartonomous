-- ============================================================================
-- GPU-Accelerated Batch Weight Atomization
-- Uses CUDA for parallel hashing and deduplication when GPU available
-- Falls back to CPU NumPy if no GPU
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_weights_gpu(
    p_weights FLOAT[],
    p_threshold FLOAT DEFAULT 1e-6
)
RETURNS TABLE(
    weight_value numeric,
    atom_id bigint,
    gpu_used boolean
) AS $$
    import json
    gpu_used = False
    
    try:
        # Try GPU acceleration with CuPy
        import cupy as cp
        import numpy as np
        
        # Convert to GPU array
        weights_gpu = cp.array(p_weights, dtype=cp.float32)
        
        # Vectorized sparse filtering on GPU
        mask = cp.abs(weights_gpu) >= p_threshold
        significant_weights_gpu = weights_gpu[mask]
        
        # Unique values on GPU (parallel)
        unique_weights_gpu = cp.unique(significant_weights_gpu)
        
        # Transfer back to CPU for SQL operations
        unique_weights = unique_weights_gpu.get().tolist()
        gpu_used = True
        
    except (ImportError, Exception) as e:
        # Fallback to CPU with NumPy SIMD
        import numpy as np
        
        weights_np = np.array(p_weights, dtype=np.float32)
        mask = np.abs(weights_np) >= p_threshold
        significant_weights = weights_np[mask]
        unique_weights = np.unique(significant_weights).tolist()
        gpu_used = False
    
    # Atomize each unique weight via SQL
    for weight in unique_weights:
        weight_hash = plpy.execute(
            f"SELECT digest('{weight}'::text, 'sha256') AS hash"
        )[0]['hash']
        
        metadata = json.dumps({
            "modality": "weight",
            "value": float(weight),
            "gpu_processed": gpu_used
        })
        
        result = plpy.execute(
            f"SELECT atomize_value('{weight_hash}'::bytea, '{weight}'::text, '{metadata}'::jsonb) AS atom_id"
        )[0]
        
        yield (weight, result['atom_id'], gpu_used)
        
$$ LANGUAGE plpython3u VOLATILE;

COMMENT ON FUNCTION atomize_weights_gpu IS
'GPU-accelerated batch weight atomization using CUDA (CuPy) when available, falls back to NumPy SIMD on CPU. Returns atom_id for each unique non-sparse weight.';
