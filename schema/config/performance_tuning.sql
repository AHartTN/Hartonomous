-- ============================================================================
-- PostgreSQL Performance Configuration
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Optimize for parallel execution, vectorization, and bulk operations
-- ============================================================================

-- Parallel Execution Settings
ALTER SYSTEM SET max_parallel_workers_per_gather = 8;  -- Use 8 workers per query
ALTER SYSTEM SET max_parallel_workers = 16;            -- Max 16 total workers
ALTER SYSTEM SET parallel_tuple_cost = 0.01;           -- Lower cost = more parallelization
ALTER SYSTEM SET parallel_setup_cost = 100;            -- Cost to start parallel worker

-- Memory Settings (for large batch operations)
ALTER SYSTEM SET work_mem = '256MB';                   -- Per-operation memory
ALTER SYSTEM SET shared_buffers = '2GB';               -- Shared cache
ALTER SYSTEM SET effective_cache_size = '8GB';         -- OS cache hint

-- JIT Compilation (LLVM - compiles hot functions to native code)
ALTER SYSTEM SET jit = on;
ALTER SYSTEM SET jit_above_cost = 100000;              -- Enable JIT for expensive queries
ALTER SYSTEM SET jit_inline_above_cost = 500000;       -- Inline functions
ALTER SYSTEM SET jit_optimize_above_cost = 500000;     -- Optimize compiled code

-- Query Planner Settings
ALTER SYSTEM SET random_page_cost = 1.1;               -- SSD-optimized
ALTER SYSTEM SET effective_io_concurrency = 200;       -- SSD parallel I/O

-- Array and Set Operations
ALTER SYSTEM SET enable_seqscan = on;                  -- Allow seq scans (parallelizable)
ALTER SYSTEM SET enable_bitmapscan = on;               -- Efficient multi-index scans
ALTER SYSTEM SET enable_hashjoin = on;                 -- Fast joins

-- PostGIS Settings
ALTER SYSTEM SET postgis.backend = 'geos';             -- GEOS backend (faster than lwgeom)
ALTER SYSTEM SET postgis.gdal_enabled_drivers = 'ENABLE_ALL';

-- LISTEN/NOTIFY Settings (for AGE sync)
ALTER SYSTEM SET max_notify_queue_pages = 1048576;     -- Large notification queue

-- Reload configuration
SELECT pg_reload_conf();

-- Verify settings
SELECT name, setting, unit
FROM pg_settings
WHERE name IN (
    'max_parallel_workers_per_gather',
    'max_parallel_workers',
    'jit',
    'work_mem',
    'shared_buffers'
)
ORDER BY name;

COMMENT ON SCHEMA public IS 
'PostgreSQL configured for maximum parallelization and vectorization.
Parallel workers: 16, JIT: enabled, Work memory: 256MB per op.';
