-- ============================================================================
-- OODA LOOP TABLES
-- Autonomous optimization tracking
-- ============================================================================

-- OODA audit log (all OODA cycle executions)
CREATE TABLE IF NOT EXISTS ooda_audit_log (
    log_id BIGSERIAL PRIMARY KEY,
    hypothesis TEXT NOT NULL,
    result TEXT NOT NULL,
    executed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

COMMENT ON TABLE ooda_audit_log IS 'Complete log of all OODA loop actions: observations, decisions, and executions';

-- OODA metrics tracking
CREATE TABLE IF NOT EXISTS ooda_metrics (
    metric_id BIGSERIAL PRIMARY KEY,
    metric_name TEXT NOT NULL,
    metric_value REAL NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

COMMENT ON TABLE ooda_metrics IS 'Time-series metrics for OODA loop performance monitoring';

-- OODA provenance (complete reasoning chain)
CREATE TABLE IF NOT EXISTS ooda_provenance (
    provenance_id BIGSERIAL PRIMARY KEY,
    observation JSONB NOT NULL,
    orientation JSONB NOT NULL,
    decision TEXT NOT NULL,
    action_result TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

COMMENT ON TABLE ooda_provenance IS 'Full OODA cycle provenance: from observation ? orientation ? decision ? action';

-- Create indexes for common queries
CREATE INDEX IF NOT EXISTS idx_ooda_metrics_name_time ON ooda_metrics(metric_name, recorded_at DESC);
CREATE INDEX IF NOT EXISTS idx_ooda_audit_executed ON ooda_audit_log(executed_at DESC);
