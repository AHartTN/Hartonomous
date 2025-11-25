-- ============================================================================
-- PL/Python3U Extension
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Enable Python in-database for ML operations
-- Libraries: NumPy, SciPy, scikit-learn, TensorFlow/PyTorch (if installed)
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS plpython3u;

COMMENT ON EXTENSION plpython3u IS 
'Python procedural language (untrusted) for in-database ML operations.
Enables: NumPy tensor ops, SciPy optimization, scikit-learn models, PyTorch inference.';
