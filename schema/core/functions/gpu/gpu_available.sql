-- Check if GPU is available
CREATE OR REPLACE FUNCTION gpu_available()
RETURNS BOOLEAN
LANGUAGE plpython3u
AS $$
    try:
        import cupy as cp
        test = cp.array([1.0])
        return True
    except:
        return False
$$;

COMMENT ON FUNCTION gpu_available() IS 
'Returns TRUE if CUDA GPU is available via CuPy, FALSE otherwise.';
