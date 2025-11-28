-- GPU-Accelerated Batch Weight Extraction
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
    
    weights_gpu = cp.array(p_weights, dtype=cp.float32)
    
    mask = cp.abs(weights_gpu) >= p_threshold
    significant_weights = weights_gpu[mask]
    
    unique_weights, counts = cp.unique(significant_weights, return_counts=True)
    
    unique_weights_cpu = unique_weights.get()
    counts_cpu = counts.get()
    
    results = []
    for weight, count in zip(unique_weights_cpu, counts_cpu):
        results.append({'weight_value': float(weight), 'occurrence_count': int(count)})
    
    return results
$$;

COMMENT ON FUNCTION extract_unique_weights_gpu(FLOAT[], FLOAT) IS
'GPU-accelerated unique weight extraction with deduplication.';
