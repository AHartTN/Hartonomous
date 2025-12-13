"""
pg_cron integration for Cortex background recalibration
Schedules continuous LMDS refinement
"""

-- Enable pg_cron extension
CREATE EXTENSION IF NOT EXISTS pg_cron;

-- Schedule Cortex recalibration every 60 seconds
SELECT cron.schedule(
    'cortex-recalibration',
    '*/1 * * * *',  -- Every minute
    $$SELECT cortex_cycle_once();$$
);

-- Schedule landmark refresh every 5 minutes
SELECT cron.schedule(
    'cortex-landmark-refresh',
    '*/5 * * * *',
    $$
    -- Refresh landmarks based on high-stress atoms
    INSERT INTO cortex_landmarks (atom_id, last_distance_update)
    SELECT atom_id, now()
    FROM atom
    WHERE atom_id IN (
        SELECT atom_id
        FROM atom
        ORDER BY RANDOM()  -- TODO: Replace with stress-based selection
        LIMIT 50
    )
    ON CONFLICT (atom_id) DO UPDATE
    SET last_distance_update = now();
    $$
);

-- Schedule stress calculation every 10 minutes
SELECT cron.schedule(
    'cortex-stress-calculation',
    '*/10 * * * *',
    $$
    -- Calculate stress for atoms based on distance discrepancy
    UPDATE cortex_state
    SET atoms_processed = (
        SELECT COUNT(*) FROM atom WHERE atom_class = 0
    ),
    last_cycle_at = now();
    $$
);

-- View scheduled jobs
SELECT * FROM cron.job;

COMMENT ON EXTENSION pg_cron IS 'Cortex background worker scheduling';
