-- Production monitoring views and metrics

-- Atom distribution metrics
CREATE OR REPLACE VIEW v_atom_stats AS
SELECT
    atom_class,
    modality,
    subtype,
    COUNT(*) as atom_count,
    AVG(CASE WHEN ST_GeometryType(geom) = 'ST_Point' THEN ST_Z(geom) ELSE NULL END) as avg_z_level,
    AVG(CASE WHEN ST_GeometryType(geom) = 'ST_Point' THEN ST_M(geom) ELSE NULL END) as avg_salience,
    MIN(created_at) as first_seen,
    MAX(updated_at) as last_updated
FROM atom
GROUP BY atom_class, modality, subtype
ORDER BY atom_count DESC;

-- Cortex performance metrics
CREATE OR REPLACE VIEW v_cortex_metrics AS
SELECT
    model_version,
    atoms_processed,
    recalibrations,
    avg_stress,
    landmark_count,
    last_cycle_at,
    EXTRACT(EPOCH FROM (now() - last_cycle_at)) as seconds_since_last_cycle
FROM cortex_state
ORDER BY model_version DESC
LIMIT 1;

-- Composition hierarchy depth
CREATE OR REPLACE VIEW v_composition_depth AS
WITH RECURSIVE comp_depth AS (
    SELECT
        atom_id,
        0 as depth
    FROM atom
    WHERE atom_class = 0
    
    UNION ALL
    
    SELECT
        c.parent_atom_id,
        cd.depth + 1
    FROM atom_compositions c
    JOIN comp_depth cd ON cd.atom_id = c.component_atom_id
)
SELECT
    MAX(depth) as max_depth,
    AVG(depth) as avg_depth,
    COUNT(DISTINCT atom_id) as total_compositions
FROM comp_depth
WHERE depth > 0;

-- Query performance tracking
CREATE TABLE IF NOT EXISTS query_metrics (
    id SERIAL PRIMARY KEY,
    query_type VARCHAR(50) NOT NULL,
    execution_time_ms DOUBLE PRECISION NOT NULL,
    result_count INTEGER,
    parameters JSONB,
    executed_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_query_metrics_type_time ON query_metrics(query_type, executed_at);

-- Log slow queries
CREATE OR REPLACE FUNCTION log_query_metric(
    p_query_type VARCHAR,
    p_execution_time_ms DOUBLE PRECISION,
    p_result_count INTEGER DEFAULT NULL,
    p_parameters JSONB DEFAULT NULL
) RETURNS VOID AS $$
BEGIN
    INSERT INTO query_metrics (query_type, execution_time_ms, result_count, parameters)
    VALUES (p_query_type, p_execution_time_ms, p_result_count, p_parameters);
    
    -- Alert on slow queries
    IF p_execution_time_ms > 1000 THEN
        RAISE WARNING 'Slow query detected: % took %.2f ms', p_query_type, p_execution_time_ms;
    END IF;
END;
$$ LANGUAGE plpgsql;

-- Index health monitoring
CREATE OR REPLACE VIEW v_index_health AS
SELECT
    schemaname,
    tablename,
    indexname,
    idx_scan as scans,
    idx_tup_read as tuples_read,
    idx_tup_fetch as tuples_fetched,
    pg_size_pretty(pg_relation_size(indexrelid)) as index_size
FROM pg_stat_user_indexes
WHERE schemaname = 'public'
ORDER BY idx_scan DESC;

-- Disk space monitoring
CREATE OR REPLACE VIEW v_disk_usage AS
SELECT
    schemaname,
    t.tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||t.tablename)) as total_size,
    pg_size_pretty(pg_relation_size(schemaname||'.'||t.tablename)) as table_size,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||t.tablename) - pg_relation_size(schemaname||'.'||t.tablename)) as indexes_size,
    (SELECT COUNT(*) FROM atom) as row_count
FROM pg_tables t
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||t.tablename) DESC;

-- Connection pool status
CREATE OR REPLACE VIEW v_connection_status AS
SELECT
    state,
    COUNT(*) as connection_count,
    MAX(state_change) as last_state_change
FROM pg_stat_activity
WHERE datname = current_database()
GROUP BY state;

-- Alert thresholds
CREATE TABLE IF NOT EXISTS monitoring_alerts (
    id SERIAL PRIMARY KEY,
    alert_type VARCHAR(50) NOT NULL,
    threshold_value DOUBLE PRECISION NOT NULL,
    current_value DOUBLE PRECISION,
    message TEXT,
    triggered_at TIMESTAMPTZ DEFAULT now(),
    resolved_at TIMESTAMPTZ
);

-- Check for alerts
CREATE OR REPLACE FUNCTION check_system_health() RETURNS TABLE(
    alert_type VARCHAR,
    severity VARCHAR,
    message TEXT
) AS $$
BEGIN
    -- Check Cortex staleness
    IF EXISTS (
        SELECT 1 FROM cortex_state
        WHERE EXTRACT(EPOCH FROM (now() - last_cycle_at)) > 300  -- 5 minutes
    ) THEN
        RETURN QUERY SELECT 'cortex_stale'::VARCHAR, 'WARNING'::VARCHAR,
            'Cortex has not run in over 5 minutes'::TEXT;
    END IF;
    
    -- Check disk space (>80% full)
    IF (SELECT pg_database_size(current_database())) > 0.8 * (
        SELECT setting::BIGINT FROM pg_settings WHERE name = 'max_wal_size'
    ) THEN
        RETURN QUERY SELECT 'disk_space'::VARCHAR, 'CRITICAL'::VARCHAR,
            'Database approaching disk space limits'::TEXT;
    END IF;
    
    -- Check for missing indexes
    IF EXISTS (
        SELECT 1 FROM pg_stat_user_tables
        WHERE schemaname = 'public'
          AND seq_scan > 10000
          AND idx_scan = 0
    ) THEN
        RETURN QUERY SELECT 'missing_index'::VARCHAR, 'WARNING'::VARCHAR,
            'Tables with high sequential scans detected'::TEXT;
    END IF;
    
    RETURN;
END;
$$ LANGUAGE plpgsql;

COMMENT ON VIEW v_atom_stats IS 'Atom distribution across modalities and classes';
COMMENT ON VIEW v_cortex_metrics IS 'Real-time Cortex performance metrics';
COMMENT ON VIEW v_composition_depth IS 'Composition hierarchy statistics';
COMMENT ON FUNCTION check_system_health IS 'System health checks and alerts';
