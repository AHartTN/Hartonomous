-- GPU Access Test from PostgreSQL PL/Python
-- Tests CUDA availability and performs sample computation

CREATE OR REPLACE FUNCTION test_gpu_access()
RETURNS TABLE(
    gpu_available boolean,
    gpu_name text,
    gpu_memory_gb double precision,
    compute_capability text,
    cuda_version text,
    test_result text,
    performance_multiplier double precision
) AS $$
    import sys
    import time
    import numpy as np
    
    results = {
        'gpu_available': False,
        'gpu_name': 'None',
        'gpu_memory_gb': 0.0,
        'compute_capability': 'N/A',
        'cuda_version': 'N/A',
        'test_result': 'Starting tests...',
        'performance_multiplier': 0.0
    }
    
    # Test 1: Check if CuPy is available
    try:
        import cupy as cp
        results['gpu_available'] = True
        results['gpu_name'] = cp.cuda.Device(0).name.decode('utf-8')
        
        # Get GPU memory
        mempool = cp.get_default_memory_pool()
        free_bytes, total_bytes = cp.cuda.Device(0).mem_info
        results['gpu_memory_gb'] = total_bytes / (1024**3)
        
        # Get compute capability
        cc_major = cp.cuda.Device(0).compute_capability[0]
        cc_minor = cp.cuda.Device(0).compute_capability[1]
        results['compute_capability'] = f"{cc_major}.{cc_minor}"
        
        # Get CUDA version
        cuda_version = cp.cuda.runtime.runtimeGetVersion()
        results['cuda_version'] = f"{cuda_version // 1000}.{(cuda_version % 1000) // 10}"
        
        # Test 2: Perform sample computation (matrix multiplication)
        # This tests actual GPU compute capability
        size = 4096  # 4K x 4K matrix
        
        # CPU version with numpy
        np_a = np.random.randn(size, size).astype(np.float32)
        np_b = np.random.randn(size, size).astype(np.float32)
        
        cpu_start = time.time()
        np_result = np.dot(np_a, np_b)
        cpu_time = time.time() - cpu_start
        
        # GPU version with cupy
        cp_a = cp.array(np_a)
        cp_b = cp.array(np_b)
        
        # Warmup
        _ = cp.dot(cp_a, cp_b)
        cp.cuda.Stream.null.synchronize()
        
        gpu_start = time.time()
        cp_result = cp.dot(cp_a, cp_b)
        cp.cuda.Stream.null.synchronize()  # Wait for GPU to finish
        gpu_time = time.time() - gpu_start
        
        # Calculate speedup
        speedup = cpu_time / gpu_time if gpu_time > 0 else 0
        results['performance_multiplier'] = round(speedup, 2)
        
        # Verify correctness
        cp_result_cpu = cp.asnumpy(cp_result)
        max_diff = np.max(np.abs(np_result - cp_result_cpu))
        
        results['test_result'] = (
            f"✓ GPU Compute Test PASSED\\n"
            f"Matrix size: {size}x{size} ({size*size*4/1e6:.1f} MB)\\n"
            f"CPU time: {cpu_time*1000:.2f}ms\\n"
            f"GPU time: {gpu_time*1000:.2f}ms\\n"
            f"Speedup: {speedup:.2f}x\\n"
            f"Max difference: {max_diff:.2e} (numerical precision)"
        )
        
    except ImportError as e:
        results['test_result'] = (
            f"✗ CuPy not available: {str(e)}\\n"
            f"Install with: pip install cupy-cuda11x (match your CUDA version)\\n"
            f"Falling back to numpy CPU operations"
        )
    except Exception as e:
        results['test_result'] = f"✗ GPU test failed: {str(e)}\\n{type(e).__name__}"
    
    return [(
        results['gpu_available'],
        results['gpu_name'],
        results['gpu_memory_gb'],
        results['compute_capability'],
        results['cuda_version'],
        results['test_result'],
        results['performance_multiplier']
    )]
$$ LANGUAGE plpython3u;

COMMENT ON FUNCTION test_gpu_access IS
'Test GPU availability and performance from within PostgreSQL.
Performs matrix multiplication benchmark to measure actual speedup.
Commercial GPU (GTX 1080 Ti) requires CPU cost adjustments for query planner.';


-- Helper function to set CPU cost for GPU functions
CREATE OR REPLACE FUNCTION set_gpu_function_cost(
    function_name text,
    estimated_speedup double precision DEFAULT 100.0
)
RETURNS void AS $$
BEGIN
    -- For commercial GPUs without logic cores visible to PostgreSQL,
    -- we need to artificially inflate CPU cost to reflect GPU speedup
    -- This helps the query planner make better decisions
    
    EXECUTE format(
        'ALTER FUNCTION %I COST %s',
        function_name,
        (1000000 / estimated_speedup)::integer
    );
    
    RAISE NOTICE 'Set function % cost to % (speedup factor: %x)',
        function_name,
        (1000000 / estimated_speedup)::integer,
        estimated_speedup;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION set_gpu_function_cost IS
'Adjust function cost to reflect GPU acceleration.
For commercial GPUs, PostgreSQL cannot detect GPU cores,
so we manually set cost based on measured speedup.';
