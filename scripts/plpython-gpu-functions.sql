-- Advanced PL/Python GPU functions using CuPy (CUDA)
-- Requires Python 3.13+ with CuPy installed

-- GPU-accelerated matrix multiplication
CREATE OR REPLACE FUNCTION gpu_matmul_cuda(
    tensor_a bytea,
    tensor_b bytea,
    shape_a int[],
    shape_b int[]
)
RETURNS bytea
LANGUAGE plpython3u
AS $$
    import numpy as np

    try:
        import cupy as cp
        use_gpu = True
    except ImportError:
        use_gpu = False

    # Convert bytea to numpy arrays
    a = np.frombuffer(tensor_a, dtype=np.float32).reshape(shape_a)
    b = np.frombuffer(tensor_b, dtype=np.float32).reshape(shape_b)

    if use_gpu:
        # Move to GPU
        a_gpu = cp.asarray(a)
        b_gpu = cp.asarray(b)

        # GPU matrix multiplication
        result_gpu = cp.matmul(a_gpu, b_gpu)

        # Move back to CPU
        result = cp.asnumpy(result_gpu)
    else:
        # Fallback to CPU
        result = np.matmul(a, b)

    return result.tobytes()
$$;

-- Batch embedding similarity using GPU
CREATE OR REPLACE FUNCTION gpu_batch_similarity(
    query_embedding float[],
    batch_embeddings float[][],
    similarity_type text DEFAULT 'cosine'
)
RETURNS float[]
LANGUAGE plpython3u
AS $$
    import numpy as np

    try:
        import cupy as cp
        use_gpu = True
    except ImportError:
        use_gpu = False

    query = np.array(query_embedding, dtype=np.float32)
    batch = np.array(batch_embeddings, dtype=np.float32)

    if use_gpu:
        query_gpu = cp.asarray(query)
        batch_gpu = cp.asarray(batch)

        if similarity_type == 'cosine':
            # Cosine similarity on GPU
            query_norm = cp.linalg.norm(query_gpu)
            batch_norms = cp.linalg.norm(batch_gpu, axis=1)
            dot_products = cp.dot(batch_gpu, query_gpu)
            similarities = dot_products / (query_norm * batch_norms + 1e-8)
        elif similarity_type == 'euclidean':
            # Euclidean distance on GPU
            diff = batch_gpu - query_gpu
            similarities = -cp.linalg.norm(diff, axis=1)
        else:
            # L2 distance (default for pgvector)
            diff = batch_gpu - query_gpu
            similarities = -cp.sum(diff * diff, axis=1)

        result = cp.asnumpy(similarities)
    else:
        # CPU fallback
        if similarity_type == 'cosine':
            query_norm = np.linalg.norm(query)
            batch_norms = np.linalg.norm(batch, axis=1)
            dot_products = np.dot(batch, query)
            result = dot_products / (query_norm * batch_norms + 1e-8)
        elif similarity_type == 'euclidean':
            diff = batch - query
            result = -np.linalg.norm(diff, axis=1)
        else:
            diff = batch - query
            result = -np.sum(diff * diff, axis=1)

    return result.tolist()
$$;

-- GPU tensor reduction operations
CREATE OR REPLACE FUNCTION gpu_tensor_reduce(
    tensor_data bytea,
    shape int[],
    operation text DEFAULT 'sum',
    axis int DEFAULT NULL
)
RETURNS float
LANGUAGE plpython3u
AS $$
    import numpy as np

    try:
        import cupy as cp
        use_gpu = True
    except ImportError:
        use_gpu = False

    data = np.frombuffer(tensor_data, dtype=np.float32).reshape(shape)

    if use_gpu:
        data_gpu = cp.asarray(data)

        if operation == 'sum':
            result = float(cp.sum(data_gpu) if axis is None else cp.sum(data_gpu, axis=axis))
        elif operation == 'mean':
            result = float(cp.mean(data_gpu) if axis is None else cp.mean(data_gpu, axis=axis))
        elif operation == 'max':
            result = float(cp.max(data_gpu) if axis is None else cp.max(data_gpu, axis=axis))
        elif operation == 'min':
            result = float(cp.min(data_gpu) if axis is None else cp.min(data_gpu, axis=axis))
        else:
            result = float(cp.sum(data_gpu))
    else:
        if operation == 'sum':
            result = float(np.sum(data) if axis is None else np.sum(data, axis=axis))
        elif operation == 'mean':
            result = float(np.mean(data) if axis is None else np.mean(data, axis=axis))
        elif operation == 'max':
            result = float(np.max(data) if axis is None else np.max(data, axis=axis))
        elif operation == 'min':
            result = float(np.min(data) if axis is None else np.min(data, axis=axis))
        else:
            result = float(np.sum(data))

    return result
$$;

-- Check GPU availability
CREATE OR REPLACE FUNCTION check_gpu_available()
RETURNS TABLE(
    gpu_available boolean,
    gpu_count int,
    gpu_name text,
    gpu_memory_gb float
)
LANGUAGE plpython3u
AS $$
    try:
        import cupy as cp

        device_count = cp.cuda.runtime.getDeviceCount()
        if device_count > 0:
            device_props = cp.cuda.runtime.getDeviceProperties(0)
            gpu_name = device_props['name'].decode('utf-8')
            gpu_memory = device_props['totalGlobalMem'] / (1024**3)  # Convert to GB

            return [(True, device_count, gpu_name, gpu_memory)]
        else:
            return [(False, 0, 'No GPU detected', 0.0)]
    except Exception as e:
        return [(False, 0, f'CuPy not available: {str(e)}', 0.0)]
$$;

COMMENT ON FUNCTION gpu_matmul_cuda IS 'GPU-accelerated matrix multiplication using CUDA via CuPy';
COMMENT ON FUNCTION gpu_batch_similarity IS 'Batch embedding similarity computation on GPU';
COMMENT ON FUNCTION gpu_tensor_reduce IS 'GPU-accelerated tensor reduction operations (sum, mean, max, min)';
COMMENT ON FUNCTION check_gpu_available IS 'Check if CUDA GPU is available and return GPU information';
