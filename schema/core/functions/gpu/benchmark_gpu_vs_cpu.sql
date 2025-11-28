-- Performance Benchmarks
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
    
    test_data = np.random.randn(p_array_size).astype(np.float32)
    
    start = time.time()
    cpu_result = np.sum(test_data ** 2)
    cpu_time = (time.time() - start) * 1000
    
    results.append({
        'test_name': 'CPU (NumPy)',
        'execution_time_ms': cpu_time,
        'speedup': '1.0x (baseline)'
    })
    
    try:
        import cupy as cp
        test_data_gpu = cp.array(test_data)
        
        start = time.time()
        gpu_result = cp.sum(test_data_gpu ** 2)
        cp.cuda.Stream.null.synchronize()
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
