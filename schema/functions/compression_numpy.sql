-- High-performance compression functions using numpy SIMD operations
-- These leverage AVX/AVX2/AVX-512 instructions through numpy

CREATE OR REPLACE FUNCTION compress_sparse_numpy(
    p_data bytea,
    p_threshold double precision DEFAULT 1e-6,
    p_dtype text DEFAULT 'float32'
) RETURNS bytea AS $$
    import numpy as np
    import struct
    
    # Convert bytea to numpy array
    data_bytes = bytes(p_data)
    
    if p_dtype == 'float64':
        arr = np.frombuffer(data_bytes, dtype=np.float64)
        dtype_size = 8
    elif p_dtype == 'float32':
        arr = np.frombuffer(data_bytes, dtype=np.float32)
        dtype_size = 4
    elif p_dtype == 'float16':
        arr = np.frombuffer(data_bytes, dtype=np.float16)
        dtype_size = 2
    else:
        return p_data
    
    # SIMD-optimized threshold comparison
    non_sparse_mask = np.abs(arr) >= p_threshold
    non_sparse_indices = np.where(non_sparse_mask)[0].astype(np.uint32)
    non_sparse_values = arr[non_sparse_mask]
    
    # Calculate compression benefit
    original_size = len(arr) * dtype_size
    compressed_size = 4 + len(non_sparse_indices) * (4 + dtype_size)  # header + (index + value) pairs
    
    # Only compress if beneficial
    if compressed_size >= original_size:
        return p_data
    
    # Format: [count:uint32][index:uint32,value:dtype]...
    result = struct.pack('I', len(non_sparse_indices))
    
    for idx, val in zip(non_sparse_indices, non_sparse_values):
        result += struct.pack('I', idx)
        if p_dtype == 'float64':
            result += struct.pack('d', val)
        elif p_dtype == 'float32':
            result += struct.pack('f', val)
        elif p_dtype == 'float16':
            result += struct.pack('e', val)
    
    return result
$$ LANGUAGE plpython3u IMMUTABLE PARALLEL SAFE;

COMMENT ON FUNCTION compress_sparse_numpy IS 
'Sparse compression using numpy SIMD operations. 
Values below threshold are discarded (treated as zero).
Returns original data if compression not beneficial.';


CREATE OR REPLACE FUNCTION decompress_sparse_numpy(
    p_compressed bytea,
    p_original_length integer,
    p_dtype text DEFAULT 'float32'
) RETURNS bytea AS $$
    import numpy as np
    import struct
    
    data_bytes = bytes(p_compressed)
    
    # Determine dtype
    if p_dtype == 'float64':
        np_dtype = np.float64
        dtype_size = 8
        struct_fmt = 'd'
    elif p_dtype == 'float32':
        np_dtype = np.float32
        dtype_size = 4
        struct_fmt = 'f'
    elif p_dtype == 'float16':
        np_dtype = np.float16
        dtype_size = 2
        struct_fmt = 'e'
    else:
        return p_compressed
    
    # Initialize zero array
    arr = np.zeros(p_original_length, dtype=np_dtype)
    
    # Read count
    count = struct.unpack('I', data_bytes[0:4])[0]
    offset = 4
    
    # Reconstruct sparse values (SIMD operations handle the array writes efficiently)
    for _ in range(count):
        idx = struct.unpack('I', data_bytes[offset:offset+4])[0]
        offset += 4
        val = struct.unpack(struct_fmt, data_bytes[offset:offset+dtype_size])[0]
        offset += dtype_size
        arr[idx] = val
    
    return arr.tobytes()
$$ LANGUAGE plpython3u IMMUTABLE PARALLEL SAFE;


CREATE OR REPLACE FUNCTION compress_delta_numpy(
    p_data bytea,
    p_dtype text DEFAULT 'float32'
) RETURNS bytea AS $$
    import numpy as np
    
    data_bytes = bytes(p_data)
    
    # Convert to numpy array
    if p_dtype == 'float64':
        arr = np.frombuffer(data_bytes, dtype=np.float64)
    elif p_dtype == 'float32':
        arr = np.frombuffer(data_bytes, dtype=np.float32)
    elif p_dtype == 'float16':
        arr = np.frombuffer(data_bytes, dtype=np.float16)
    else:
        return p_data
    
    if len(arr) < 2:
        return p_data
    
    # SIMD-optimized delta calculation: deltas[i] = arr[i+1] - arr[i]
    # Keep first value, then store differences
    first_val = arr[0:1]
    deltas = np.diff(arr)  # Vectorized subtraction
    
    # Combine and convert back
    result = np.concatenate([first_val, deltas])
    return result.tobytes()
$$ LANGUAGE plpython3u IMMUTABLE PARALLEL SAFE;

COMMENT ON FUNCTION compress_delta_numpy IS
'Delta encoding using numpy SIMD operations.
Stores first value + sequential differences.
Reduces magnitude for values with temporal/spatial correlation.';


CREATE OR REPLACE FUNCTION decompress_delta_numpy(
    p_compressed bytea,
    p_dtype text DEFAULT 'float32'
) RETURNS bytea AS $$
    import numpy as np
    
    data_bytes = bytes(p_compressed)
    
    # Convert to numpy array
    if p_dtype == 'float64':
        arr = np.frombuffer(data_bytes, dtype=np.float64)
    elif p_dtype == 'float32':
        arr = np.frombuffer(data_bytes, dtype=np.float32)
    elif p_dtype == 'float16':
        arr = np.frombuffer(data_bytes, dtype=np.float16)
    else:
        return p_compressed
    
    # SIMD-optimized cumulative sum to reconstruct original
    result = np.cumsum(arr)
    return result.tobytes()
$$ LANGUAGE plpython3u IMMUTABLE PARALLEL SAFE;


CREATE OR REPLACE FUNCTION compress_rle_numpy(
    p_data bytea,
    p_min_run_length integer DEFAULT 3
) RETURNS bytea AS $$
    import numpy as np
    import struct
    
    data_bytes = bytes(p_data)
    arr = np.frombuffer(data_bytes, dtype=np.uint8)
    
    if len(arr) < p_min_run_length:
        return p_data
    
    # Find runs using SIMD-accelerated comparison
    # arr[1:] != arr[:-1] gives boundaries between runs
    run_boundaries = np.concatenate(([0], np.where(arr[1:] != arr[:-1])[0] + 1, [len(arr)]))
    
    result = []
    compressed = False
    
    for i in range(len(run_boundaries) - 1):
        start = run_boundaries[i]
        end = run_boundaries[i + 1]
        run_length = end - start
        value = int(arr[start])
        
        if run_length >= p_min_run_length:
            # Encode as RLE: [marker:0xFF][value:uint8][count:uint32]
            result.append(struct.pack('BBI', 0xFF, value, run_length))
            compressed = True
        else:
            # Store literal values
            result.append(arr[start:end].tobytes())
    
    if not compressed:
        return p_data
    
    return b''.join(result)
$$ LANGUAGE plpython3u IMMUTABLE PARALLEL SAFE;

COMMENT ON FUNCTION compress_rle_numpy IS
'Run-length encoding using numpy vectorized operations.
Encodes repeated byte sequences.
Format: 0xFF marker + value + count for runs, literal bytes otherwise.';


CREATE OR REPLACE FUNCTION decompress_rle_numpy(
    p_compressed bytea
) RETURNS bytea AS $$
    import numpy as np
    import struct
    
    data_bytes = bytes(p_compressed)
    result = []
    offset = 0
    
    while offset < len(data_bytes):
        if data_bytes[offset] == 0xFF and offset + 5 <= len(data_bytes):
            # RLE marker found
            value = data_bytes[offset + 1]
            count = struct.unpack('I', data_bytes[offset+2:offset+6])[0]
            
            # SIMD-efficient: create array of repeated value
            result.append(np.full(count, value, dtype=np.uint8).tobytes())
            offset += 6
        else:
            # Literal byte
            result.append(bytes([data_bytes[offset]]))
            offset += 1
    
    return b''.join(result)
$$ LANGUAGE plpython3u IMMUTABLE PARALLEL SAFE;


CREATE OR REPLACE FUNCTION compress_multi_layer(
    p_data bytea,
    p_sparse_threshold double precision DEFAULT 1e-6,
    p_dtype text DEFAULT 'float32',
    p_enable_delta boolean DEFAULT true,
    p_enable_rle boolean DEFAULT true
) RETURNS TABLE(
    compressed_data bytea,
    encoding_chain text[],
    compression_ratio double precision,
    original_size integer,
    compressed_size integer
) AS $$
    import numpy as np
    
    original = bytes(p_data)
    current = original
    encodings = []
    original_size = len(original)
    
    # Layer 1: Sparse encoding (for floating point data)
    try:
        sparse_result = plpy.execute(
            plpy.prepare(
                "SELECT compress_sparse_numpy($1, $2, $3) AS result",
                ["bytea", "double precision", "text"]
            ),
            [current, p_sparse_threshold, p_dtype]
        )[0]['result']
        
        if len(bytes(sparse_result)) < len(current):
            current = bytes(sparse_result)
            encodings.append('sparse')
    except:
        pass  # Sparse encoding failed, continue
    
    # Layer 2: Delta encoding (if enabled)
    if p_enable_delta and len(current) >= 16:
        try:
            delta_result = plpy.execute(
                plpy.prepare(
                    "SELECT compress_delta_numpy($1, $2) AS result",
                    ["bytea", "text"]
                ),
                [current, p_dtype]
            )[0]['result']
            
            if len(bytes(delta_result)) < len(current):
                current = bytes(delta_result)
                encodings.append('delta')
        except:
            pass  # Delta encoding failed, continue
    
    # Layer 3: RLE encoding (if enabled)
    if p_enable_rle:
        try:
            rle_result = plpy.execute(
                plpy.prepare(
                    "SELECT compress_rle_numpy($1) AS result",
                    ["bytea"]
                ),
                [current]
            )[0]['result']
            
            if len(bytes(rle_result)) < len(current):
                current = bytes(rle_result)
                encodings.append('rle')
        except:
            pass  # RLE encoding failed, continue
    
    # Calculate compression ratio
    compressed_size = len(current)
    ratio = original_size / compressed_size if compressed_size > 0 else 1.0
    
    return [(current, encodings, ratio, original_size, compressed_size)]
$$ LANGUAGE plpython3u IMMUTABLE PARALLEL SAFE;

COMMENT ON FUNCTION compress_multi_layer IS
'Apply multiple compression layers in sequence:
1. Sparse encoding (discard near-zero values)
2. Delta encoding (store differences)
3. Run-length encoding (compress repeated patterns)
Each layer only applied if it improves compression.
All operations use numpy SIMD vectorization.';
