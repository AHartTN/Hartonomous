-- Migration: Add performance monitoring views
-- Version: 1.0.0
-- Date: 2025-12-13

-- Drop existing views for idempotent deployment
DROP VIEW IF EXISTS v_query_performance CASCADE;
DROP VIEW IF EXISTS v_index_usage CASCADE;
DROP VIEW IF EXISTS v_atom_distribution CASCADE;
DROP VIEW IF EXISTS v_hierarchy_stats CASCADE;
DROP MATERIALIZED VIEW IF EXISTS mv_atom_clusters CASCADE;

-- View: Spatial query performance
CREATE VIEW v_query_performance AS
SELECT
    query,
    calls,
    total_exec_time,
    mean_exec_time,
    stddev_exec_time,
    min_exec_time,
    max_exec_time
FROM pg_stat_statements
WHERE query LIKE '%atom%'
    AND query LIKE '%ORDER BY%geom%'
ORDER BY mean_exec_time DESC;

-- View: Index usage statistics
CREATE OR REPLACE VIEW v_index_usage AS
SELECT
    schemaname,
    tablename,
    indexname,
    idx_scan,
    idx_tup_read,
    idx_tup_fetch,
    pg_size_pretty(pg_relation_size(indexrelid)) as index_size
FROM pg_stat_user_indexes
WHERE schemaname = 'public'
ORDER BY idx_scan DESC;

-- View: Table bloat monitoring
CREATE OR REPLACE VIEW v_table_bloat AS
SELECT
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as total_size,
    pg_size_pretty(pg_relation_size(schemaname||'.'||tablename)) as table_size,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename) - pg_relation_size(schemaname||'.'||tablename)) as index_size,
    n_dead_tup,
    n_live_tup,
    CASE 
        WHEN n_live_tup > 0 
        THEN round(100.0 * n_dead_tup / n_live_tup, 2)
        ELSE 0
    END as dead_tuple_percent
FROM pg_stat_user_tables
WHERE schemaname = 'public'
ORDER BY n_dead_tup DESC;

-- View: Cortex monitoring
CREATE OR REPLACE VIEW v_cortex_status AS
SELECT
    cs.model_version,
    cs.atoms_processed,
    cs.recalibrations,
    cs.avg_stress,
    cs.last_cycle_at,
    cs.landmark_count,
    EXTRACT(EPOCH FROM (now() - cs.last_cycle_at)) as seconds_since_cycle,
    (SELECT COUNT(*) FROM atom WHERE atom_class = 0) as constant_count,
    (SELECT COUNT(*) FROM atom WHERE atom_class = 1) as composition_count
FROM cortex_state cs
WHERE cs.id = 1;

-- Function: Get spatial query plan
CREATE OR REPLACE FUNCTION explain_spatial_query(
    target_atom BYTEA,
    k INTEGER DEFAULT 10
)
RETURNS TABLE(plan_line TEXT) AS $$
BEGIN
    RETURN QUERY EXECUTE format(
        'EXPLAIN (ANALYZE, BUFFERS) '
        'WITH target AS (SELECT geom FROM atom WHERE atom_id = %L) '
        'SELECT atom_id FROM atom, target '
        'WHERE atom_id != %L '
        'ORDER BY geom <-> target.geom LIMIT %s',
        target_atom, target_atom, k
    );
END;
$$ LANGUAGE plpgsql;

-- Function: Vacuum and analyze atoms table
CREATE OR REPLACE FUNCTION maintain_atoms_table()
RETURNS void AS $$
BEGIN
    VACUUM ANALYZE atom;
    REINDEX TABLE atom;
    ANALYZE atom;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION maintain_atoms_table() IS 
'Perform routine maintenance on atom table';
