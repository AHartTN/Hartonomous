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
