-- Load balancing and connection pooling configuration

-- Connection pool metrics view
CREATE OR REPLACE VIEW v_connection_pool_stats AS
SELECT
    datname,
    usename,
    application_name,
    COUNT(*) as connection_count,
    COUNT(*) FILTER (WHERE state = 'active') as active_connections,
    COUNT(*) FILTER (WHERE state = 'idle') as idle_connections,
    COUNT(*) FILTER (WHERE state = 'idle in transaction') as idle_in_transaction,
    AVG(EXTRACT(EPOCH FROM (now() - state_change))) as avg_connection_age_sec
FROM pg_stat_activity
WHERE datname = 'hartonomous'
GROUP BY datname, usename, application_name;

-- Query load distribution
CREATE OR REPLACE VIEW v_query_load_distribution AS
SELECT
    CASE
        WHEN query LIKE 'SELECT%<->%' THEN 'knn_query'
        WHEN query LIKE 'SELECT%ST_DWithin%' THEN 'radius_search'
        WHEN query LIKE 'SELECT%cortex_cycle%' THEN 'cortex_cycle'
        WHEN query LIKE 'INSERT INTO atom%' THEN 'atom_insert'
        WHEN query LIKE 'COPY atom%' THEN 'bulk_load'
        ELSE 'other'
    END as query_type,
    COUNT(*) as execution_count,
    AVG(EXTRACT(EPOCH FROM (now() - query_start)) * 1000) as avg_duration_ms,
    MAX(EXTRACT(EPOCH FROM (now() - query_start)) * 1000) as max_duration_ms
FROM pg_stat_activity
WHERE datname = 'hartonomous'
  AND state = 'active'
  AND query_start IS NOT NULL
GROUP BY query_type
ORDER BY execution_count DESC;

-- Connection limit enforcement
CREATE OR REPLACE FUNCTION enforce_connection_limits()
RETURNS TRIGGER AS $$
DECLARE
    current_connections INTEGER;
    max_connections INTEGER := 100;
BEGIN
    SELECT COUNT(*) INTO current_connections
    FROM pg_stat_activity
    WHERE datname = current_database()
      AND pid != pg_backend_pid();
    
    IF current_connections >= max_connections THEN
        RAISE EXCEPTION 'Connection pool exhausted: % active connections', current_connections
            USING HINT = 'Increase max_connections or implement connection pooling';
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Read/write split routing hints
CREATE OR REPLACE FUNCTION is_read_only_query(query_text TEXT)
RETURNS BOOLEAN AS $$
BEGIN
    RETURN query_text ~* '^SELECT'
       AND NOT query_text ~* 'FOR UPDATE|FOR SHARE'
       AND NOT query_text ~* 'nextval|setval';
END;
$$ LANGUAGE plpgsql IMMUTABLE;

-- Hot standby lag monitoring
CREATE OR REPLACE VIEW v_replication_lag AS
SELECT
    client_addr,
    application_name,
    state,
    sync_state,
    pg_wal_lsn_diff(pg_current_wal_lsn(), sent_lsn) / 1024.0 as send_lag_kb,
    pg_wal_lsn_diff(pg_current_wal_lsn(), replay_lsn) / 1024.0 as replay_lag_kb,
    EXTRACT(EPOCH FROM (now() - pg_last_wal_receive_lsn())) as receive_lag_seconds
FROM pg_stat_replication;

-- Session-level query timeout
ALTER DATABASE hartonomous SET statement_timeout = '30s';
ALTER DATABASE hartonomous SET idle_in_transaction_session_timeout = '60s';

-- pgBouncer configuration template (save to pgbouncer.ini)
/*
[databases]
hartonomous = host=localhost port=5432 dbname=hartonomous

[pgbouncer]
pool_mode = transaction
max_client_conn = 1000
default_pool_size = 25
reserve_pool_size = 5
reserve_pool_timeout = 3
max_db_connections = 100
max_user_connections = 100

listen_addr = 0.0.0.0
listen_port = 6432
auth_type = md5
auth_file = /etc/pgbouncer/userlist.txt

server_idle_timeout = 600
server_lifetime = 3600
server_connect_timeout = 15
query_timeout = 30

log_connections = 1
log_disconnections = 1
log_pooler_errors = 1
*/

COMMENT ON VIEW v_connection_pool_stats IS 'Real-time connection pool utilization';
COMMENT ON VIEW v_query_load_distribution IS 'Query type distribution for load balancing';
COMMENT ON FUNCTION enforce_connection_limits IS 'Prevent connection pool exhaustion';
