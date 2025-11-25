-- ============================================================================
-- PL/Python Extension Configuration
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- PL/Python: Python procedural language for PostgreSQL
-- Enables GPU acceleration via PyTorch/CuPy for scientific computing
CREATE EXTENSION IF NOT EXISTS plpython3u;

COMMENT ON EXTENSION plpython3u IS 
'PL/Python3U - Python procedural language (untrusted) for GPU acceleration';
