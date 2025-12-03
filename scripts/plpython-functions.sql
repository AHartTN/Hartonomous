-- PL/Python functions for GPU-accelerated tensor operations

-- Matrix multiplication using GPU (CuPy)
CREATE OR REPLACE FUNCTION gpu_matmul(
    tensor_a bytea,
    tensor_b bytea,
    shape_a int[],
    shape_b int[]
)
RETURNS bytea
LANGUAGE plpython3u
AS $$
    import numpy as np

    # For now, CPU implementation - GPU version requires CuPy
    # Convert bytea to numpy arrays
    a = np.frombuffer(tensor_a, dtype=np.float32).reshape(shape_a)
    b = np.frombuffer(tensor_b, dtype=np.float32).reshape(shape_b)

    # Perform matrix multiplication
    result = np.matmul(a, b)

    # Convert back to bytes
    return result.tobytes()
$$;

-- Compute embedding similarity using cosine distance
CREATE OR REPLACE FUNCTION embedding_cosine_similarity(
    embedding_a float[],
    embedding_b float[]
)
RETURNS float
LANGUAGE plpython3u
AS $$
    import numpy as np

    a = np.array(embedding_a)
    b = np.array(embedding_b)

    dot_product = np.dot(a, b)
    norm_a = np.linalg.norm(a)
    norm_b = np.linalg.norm(b)

    if norm_a == 0 or norm_b == 0:
        return 0.0

    return float(dot_product / (norm_a * norm_b))
$$;

-- Tensor decomposition and chunking
CREATE OR REPLACE FUNCTION tensor_chunk_decompose(
    tensor_data bytea,
    shape int[],
    chunk_size int DEFAULT 32
)
RETURNS TABLE(
    chunk_start int[],
    chunk_end int[],
    chunk_data bytea
)
LANGUAGE plpython3u
AS $$
    import numpy as np

    # Convert bytea to numpy array
    data = np.frombuffer(tensor_data, dtype=np.float32).reshape(shape)

    # Generate chunks
    chunks = []
    for i in range(0, shape[0], chunk_size):
        for j in range(0, shape[1] if len(shape) > 1 else 1, chunk_size):
            chunk_start = [i, j if len(shape) > 1 else 0]
            chunk_end = [
                min(i + chunk_size, shape[0]),
                min(j + chunk_size, shape[1]) if len(shape) > 1 else 1
            ]

            if len(shape) > 1:
                chunk = data[i:chunk_end[0], j:chunk_end[1]]
            else:
                chunk = data[i:chunk_end[0]]

            chunks.append((chunk_start, chunk_end, chunk.tobytes()))

    return chunks
$$;

-- Compute SHA256 hash for content-addressable storage
CREATE OR REPLACE FUNCTION compute_content_hash(data bytea)
RETURNS text
LANGUAGE plpython3u
AS $$
    import hashlib
    return hashlib.sha256(data).hexdigest().upper()
$$;

COMMENT ON FUNCTION gpu_matmul IS 'GPU-accelerated matrix multiplication using PL/Python';
COMMENT ON FUNCTION embedding_cosine_similarity IS 'Compute cosine similarity between two embeddings';
COMMENT ON FUNCTION tensor_chunk_decompose IS 'Decompose a tensor into chunks for storage';
COMMENT ON FUNCTION compute_content_hash IS 'Compute SHA256 hash for content-addressable storage';
