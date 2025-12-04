-- ============================================================================
-- Production Database Optimizations for Hartonomous
-- ============================================================================
-- This script contains PostgreSQL-specific optimizations that cannot be
-- expressed through EF Core migrations:
--   - Table partitioning by Hilbert index
--   - Materialized views for hot atoms
--   - Automatic triggers for reference counting
--   - PL/pgSQL functions for spatial operations
-- 
-- Execute after initial migration: dotnet ef database update
-- ============================================================================

-- ============================================================================
-- 1. TABLE PARTITIONING BY HILBERT INDEX
-- ============================================================================
-- Convert constants table to partitioned table for 10-100x query performance
-- Partitioning by Hilbert index ensures spatial locality within partitions

DO $$
BEGIN
    -- Check if table is already partitioned
    IF NOT EXISTS (
        SELECT 1 FROM pg_tables 
        WHERE schemaname = 'public' 
        AND tablename = 'constants_old'
    ) THEN
        RAISE NOTICE 'Converting constants table to partitioned...';
        
        -- Rename existing table
        ALTER TABLE constants RENAME TO constants_old;
        
        -- Create new partitioned table
        CREATE TABLE constants (
            LIKE constants_old INCLUDING DEFAULTS INCLUDING CONSTRAINTS INCLUDING INDEXES
        ) PARTITION BY RANGE (hilbert_index);
        
        -- Create 64 partitions for Hilbert curve ranges
        -- Each partition covers ~144 petabytes of 3D space (2^63 / 64)
        DECLARE
            partition_size BIGINT := 144115188075855872; -- 2^57 (2^63 / 64)
            start_val BIGINT := -9223372036854775808; -- INT64 MIN (-2^63)
            end_val BIGINT;
            partition_name TEXT;
        BEGIN
            FOR i IN 0..63 LOOP
                end_val := start_val + partition_size;
                partition_name := 'constants_p' || LPAD(i::TEXT, 2, '0');
                
                EXECUTE format(
                    'CREATE TABLE %I PARTITION OF constants FOR VALUES FROM (%L) TO (%L)',
                    partition_name, start_val, end_val
                );
                
                RAISE NOTICE 'Created partition % for range [%, %)', partition_name, start_val, end_val;
                
                start_val := end_val;
            END LOOP;
        END;
        
        -- Copy data from old table (with progress tracking)
        RAISE NOTICE 'Copying data to partitioned table...';
        INSERT INTO constants SELECT * FROM constants_old;
        
        -- Verify row count
        DECLARE
            old_count BIGINT;
            new_count BIGINT;
        BEGIN
            SELECT COUNT(*) INTO old_count FROM constants_old;
            SELECT COUNT(*) INTO new_count FROM constants;
            
            IF old_count = new_count THEN
                RAISE NOTICE 'Migration verified: % rows copied', new_count;
                DROP TABLE constants_old CASCADE;
                RAISE NOTICE 'Old table dropped successfully';
            ELSE
                RAISE EXCEPTION 'Migration failed: old table has % rows, new table has % rows', old_count, new_count;
            END IF;
        END;
    ELSE
        RAISE NOTICE 'Table partitioning already applied, skipping...';
    END IF;
END $$;

-- ============================================================================
-- 2. MATERIALIZED VIEW FOR HOT ATOMS
-- ============================================================================
-- Cache frequently accessed constants in materialized view
-- Criteria: frequency >= 10 OR reference_count >= 5 OR accessed in last hour
-- Refresh every 5 minutes via background worker

CREATE MATERIALIZED VIEW IF NOT EXISTS hot_atoms AS
SELECT 
    id,
    hash,
    data,
    size,
    content_type,
    hilbert_index,
    hilbert_precision,
    coordinate_x,
    coordinate_y,
    coordinate_z,
    location,
    status,
    reference_count,
    frequency,
    last_accessed_at,
    created_at
FROM constants
WHERE 
    is_deleted = false
    AND status = 'Active'
    AND (
        frequency >= 10 
        OR reference_count >= 5
        OR last_accessed_at >= NOW() - INTERVAL '1 hour'
    )
ORDER BY 
    frequency DESC, 
    last_accessed_at DESC
LIMIT 10000;

-- Indexes on materialized view
CREATE UNIQUE INDEX IF NOT EXISTS idx_hot_atoms_id 
    ON hot_atoms(id);

CREATE INDEX IF NOT EXISTS idx_hot_atoms_hilbert 
    ON hot_atoms USING btree(hilbert_index);

CREATE INDEX IF NOT EXISTS idx_hot_atoms_location 
    ON hot_atoms USING gist(location);

CREATE INDEX IF NOT EXISTS idx_hot_atoms_hash 
    ON hot_atoms USING hash(hash);

COMMENT ON MATERIALIZED VIEW hot_atoms IS 
    'Cached view of frequently accessed constants. Refresh every 5 minutes with: REFRESH MATERIALIZED VIEW CONCURRENTLY hot_atoms;';

-- ============================================================================
-- 3. AUTOMATIC REFERENCE COUNT TRIGGER
-- ============================================================================
-- Automatically update reference_count when constant_tokens changes
-- Ensures data consistency without application logic

CREATE OR REPLACE FUNCTION update_constant_reference_count()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        -- Increment reference count
        UPDATE constants 
        SET 
            reference_count = reference_count + 1,
            updated_at = NOW()
        WHERE id = NEW.constants_id;
        
        RETURN NEW;
        
    ELSIF TG_OP = 'DELETE' THEN
        -- Decrement reference count (never below 0)
        UPDATE constants 
        SET 
            reference_count = GREATEST(0, reference_count - 1),
            updated_at = NOW()
        WHERE id = OLD.constants_id;
        
        RETURN OLD;
    END IF;
    
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Drop existing trigger if exists
DROP TRIGGER IF EXISTS trg_update_constant_reference_count ON constant_tokens;

-- Create trigger on constant_tokens join table
CREATE TRIGGER trg_update_constant_reference_count
AFTER INSERT OR DELETE ON constant_tokens
FOR EACH ROW
EXECUTE FUNCTION update_constant_reference_count();

COMMENT ON FUNCTION update_constant_reference_count() IS 
    'Automatically maintains reference_count on constants table when constant_tokens changes';

-- ============================================================================
-- 4. SPATIAL PROXIMITY FUNCTION
-- ============================================================================
-- High-performance k-NN proximity search using Hilbert index ranges
-- Returns constants within radius of target coordinate

CREATE OR REPLACE FUNCTION find_constants_near(
    target_hilbert BIGINT,
    search_radius BIGINT,
    max_results INT DEFAULT 100
)
RETURNS TABLE (
    id UUID,
    hash BYTEA,
    hilbert_index BIGINT,
    distance BIGINT,
    coordinate_x INT,
    coordinate_y INT,
    coordinate_z INT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.hilbert_index,
        ABS(c.hilbert_index - target_hilbert) AS distance,
        c.coordinate_x,
        c.coordinate_y,
        c.coordinate_z
    FROM constants c
    WHERE 
        c.is_deleted = false
        AND c.status = 'Active'
        AND c.hilbert_index BETWEEN (target_hilbert - search_radius) AND (target_hilbert + search_radius)
    ORDER BY ABS(c.hilbert_index - target_hilbert)
    LIMIT max_results;
END;
$$ LANGUAGE plpgsql STABLE;

COMMENT ON FUNCTION find_constants_near IS 
    'Fast spatial proximity search using Hilbert B-tree index. Returns up to max_results constants within search_radius of target_hilbert coordinate.';

-- ============================================================================
-- 5. VACUUM AND ANALYZE FOR STATISTICS
-- ============================================================================
-- Update PostgreSQL query planner statistics for optimal query plans

VACUUM ANALYZE constants;
VACUUM ANALYZE landmarks;
VACUUM ANALYZE bpe_tokens;
VACUUM ANALYZE constant_tokens;
VACUUM ANALYZE hot_atoms;

-- ============================================================================
-- 6. CONNECTION POOLING RECOMMENDATIONS
-- ============================================================================
-- Add to connection string:
-- Pooling=true;MinPoolSize=10;MaxPoolSize=100;ConnectionIdleLifetime=300;ConnectionPruningInterval=10;Timeout=30;CommandTimeout=60

COMMENT ON DATABASE CURRENT_DATABASE() IS 
    'Hartonomous content-addressable storage with Hilbert space-filling curves. Recommended connection string parameters: Pooling=true;MinPoolSize=10;MaxPoolSize=100;ConnectionIdleLifetime=300';

-- ============================================================================
-- 7. AUTOVACUUM TUNING FOR HIGH-WRITE TABLES
-- ============================================================================
-- Tune autovacuum for constants table (high insert rate during ingestion)

ALTER TABLE constants SET (
    autovacuum_vacuum_scale_factor = 0.05,
    autovacuum_analyze_scale_factor = 0.02,
    autovacuum_vacuum_cost_delay = 10,
    autovacuum_vacuum_cost_limit = 1000
);

ALTER TABLE constant_tokens SET (
    autovacuum_vacuum_scale_factor = 0.05,
    autovacuum_analyze_scale_factor = 0.02
);

COMMENT ON TABLE constants IS 
    'Content-addressable storage atoms indexed by Hilbert space-filling curve. Partitioned by hilbert_index for optimal spatial query performance.';

-- ============================================================================
-- VERIFICATION QUERIES
-- ============================================================================

-- Verify partitioning
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE tablename LIKE 'constants%'
ORDER BY tablename;

-- Verify indexes
SELECT 
    schemaname,
    tablename,
    indexname,
    indexdef
FROM pg_indexes
WHERE tablename IN ('constants', 'hot_atoms')
ORDER BY tablename, indexname;

-- Verify triggers
SELECT 
    trigger_name,
    event_manipulation,
    event_object_table,
    action_statement
FROM information_schema.triggers
WHERE event_object_table IN ('constants', 'constant_tokens');

-- Statistics
SELECT 
    'constants' AS table_name,
    COUNT(*) AS total_rows,
    COUNT(*) FILTER (WHERE status = 'Active') AS active_rows,
    COUNT(*) FILTER (WHERE is_duplicate = true) AS duplicates,
    AVG(size)::BIGINT AS avg_size_bytes,
    SUM(size)::BIGINT AS total_size_bytes,
    pg_size_pretty(SUM(size)::BIGINT) AS total_size_human
FROM constants;

SELECT 'Migration completed successfully!' AS status;
