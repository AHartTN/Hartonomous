# OODA LOOP

Continuous self-optimization. Observe-Orient-Decide-Act.

---

## Core Concept

System monitors own performance, detects inefficiencies, hypothesizes optimizations, executes changes autonomously.

**Traditional AI**: Static after training
**Hartonomous**: Continuously improves via feedback loop

---

## The Loop

```
OBSERVE → ORIENT → DECIDE → ACT → (repeat)
   ↓         ↓        ↓       ↓
Metrics   Analyze  Hypothesis  Execute
```

**Frequency**: Every N queries, or time-based (hourly, daily).

---

## Phase 1: OBSERVE

Collect performance metrics:

```sql
CREATE FUNCTION ooda_observe()
RETURNS TABLE(issue TEXT, metric REAL, atom_id BIGINT) AS $$
BEGIN
    RETURN QUERY

    -- Slow queries (missing indexes?)
    SELECT
        'slow_spatial_query' AS issue,
        AVG(query_time_ms)::REAL AS metric,
        NULL::BIGINT AS atom_id
    FROM pg_stat_statements
    WHERE query LIKE '%spatial_key%'
      AND mean_exec_time > 100;

    UNION ALL

    -- Heavy atoms (high reference_count, slow lookups?)
    SELECT
        'heavy_atom' AS issue,
        reference_count::REAL AS metric,
        atom_id
    FROM atom
    WHERE reference_count > 1000000
    ORDER BY reference_count DESC
    LIMIT 10;

    UNION ALL

    -- Sparse regions (underutilized space?)
    SELECT
        'sparse_region' AS issue,
        COUNT(*)::REAL AS density,
        MIN(atom_id) AS representative_atom
    FROM atom
    GROUP BY ST_SnapToGrid(spatial_key, 0.5)
    HAVING COUNT(*) < 10;

    UNION ALL

    -- Relation weight drift (unused synapses?)
    SELECT
        'weak_synapse' AS issue,
        AVG(weight)::REAL AS metric,
        source_atom_id
    FROM atom_relation
    GROUP BY source_atom_id
    HAVING AVG(weight) < 0.1;

END;
$$ LANGUAGE plpgsql;
```

---

## Phase 2: ORIENT

Analyze root causes:

```sql
CREATE FUNCTION ooda_orient(p_issue TEXT, p_metric REAL, p_atom_id BIGINT)
RETURNS TABLE(root_cause TEXT, recommendation TEXT) AS $$
BEGIN
    IF p_issue = 'slow_spatial_query' THEN
        RETURN QUERY
        SELECT
            'Missing GIST index'::TEXT,
            'CREATE INDEX idx_missing ON atom USING GIST (spatial_key)'::TEXT;

    ELSIF p_issue = 'heavy_atom' THEN
        RETURN QUERY
        SELECT
            'Atom referenced ' || p_metric::TEXT || ' times',
            'Materialize spatial position or partition table'::TEXT;

    ELSIF p_issue = 'sparse_region' THEN
        RETURN QUERY
        SELECT
            'Low-density region detected',
            'Consolidate atoms or mark region as exploratory'::TEXT;

    ELSIF p_issue = 'weak_synapse' THEN
        RETURN QUERY
        SELECT
            'Synapse weight decayed below threshold',
            'DELETE FROM atom_relation WHERE weight < 0.05'::TEXT;

    END IF;
END;
$$ LANGUAGE plpgsql;
```

---

## Phase 3: DECIDE

Generate hypothesis (DDL/DML):

```sql
CREATE FUNCTION ooda_decide(p_recommendation TEXT)
RETURNS TEXT AS $$
DECLARE
    v_hypothesis TEXT;
BEGIN
    -- Parse recommendation, generate executable SQL
    IF p_recommendation LIKE '%CREATE INDEX%' THEN
        v_hypothesis := p_recommendation;

    ELSIF p_recommendation LIKE '%partition table%' THEN
        v_hypothesis := 'CREATE TABLE atom_hot PARTITION OF atom FOR VALUES FROM (1000000) TO (MAXVALUE)';

    ELSIF p_recommendation LIKE '%DELETE FROM atom_relation%' THEN
        v_hypothesis := 'DELETE FROM atom_relation WHERE weight < 0.05';

    ELSE
        v_hypothesis := NULL;  -- Unknown recommendation
    END IF;

    RETURN v_hypothesis;
END;
$$ LANGUAGE plpgsql;
```

---

## Phase 4: ACT

Execute optimization:

```sql
CREATE FUNCTION ooda_act(p_hypothesis TEXT)
RETURNS TEXT AS $$
DECLARE
    v_result TEXT;
BEGIN
    -- Safety check: require manual approval for DROP, ALTER
    IF p_hypothesis LIKE '%DROP%' OR p_hypothesis LIKE '%ALTER%' THEN
        RETURN 'REQUIRES_APPROVAL: ' || p_hypothesis;
    END IF;

    -- Execute
    BEGIN
        EXECUTE p_hypothesis;
        v_result := 'SUCCESS: ' || p_hypothesis;
    EXCEPTION WHEN OTHERS THEN
        v_result := 'FAILED: ' || SQLERRM;
    END;

    -- Log action
    INSERT INTO ooda_audit_log (hypothesis, result, executed_at)
    VALUES (p_hypothesis, v_result, now());

    RETURN v_result;
END;
$$ LANGUAGE plpgsql;
```

---

## Audit Log

Track all OODA actions:

```sql
CREATE TABLE ooda_audit_log (
    log_id BIGSERIAL PRIMARY KEY,
    hypothesis TEXT,
    result TEXT,
    executed_at TIMESTAMPTZ DEFAULT now()
);

-- Query recent optimizations
SELECT * FROM ooda_audit_log
ORDER BY executed_at DESC
LIMIT 100;
```

---

## Full OODA Cycle

```sql
-- Run complete loop
DO $$
DECLARE
    obs RECORD;
    orient RECORD;
    hypothesis TEXT;
    result TEXT;
BEGIN
    -- OBSERVE
    FOR obs IN SELECT * FROM ooda_observe() LOOP

        -- ORIENT
        FOR orient IN SELECT * FROM ooda_orient(obs.issue, obs.metric, obs.atom_id) LOOP

            -- DECIDE
            hypothesis := ooda_decide(orient.recommendation);

            IF hypothesis IS NOT NULL THEN
                -- ACT
                result := ooda_act(hypothesis);
                RAISE NOTICE 'OODA: % → %', hypothesis, result;
            END IF;

        END LOOP;
    END LOOP;
END $$;
```

---

## Hebbian Learning Integration

Strengthen synapses that fire together:

```sql
-- Reinforce frequently co-occurring atoms
CREATE FUNCTION hebbian_reinforcement()
RETURNS VOID AS $$
BEGIN
    -- Find atom pairs that co-occur in queries
    UPDATE atom_relation ar
    SET weight = LEAST(weight * 1.05, 1.0)  -- Cap at 1.0
    WHERE EXISTS (
        SELECT 1 FROM query_log ql
        WHERE ql.atoms @> ARRAY[ar.source_atom_id, ar.target_atom_id]
          AND ql.executed_at > now() - INTERVAL '1 hour'
    );

    -- Decay unused synapses
    UPDATE atom_relation
    SET weight = weight * 0.95
    WHERE relation_id NOT IN (
        SELECT DISTINCT relation_id FROM query_log
        WHERE executed_at > now() - INTERVAL '1 day'
    );
END;
$$ LANGUAGE plpgsql;
```

---

## Spatial Drift Detection

Monitor if atom positions diverge from consensus:

```sql
CREATE FUNCTION detect_spatial_drift()
RETURNS TABLE(atom_id BIGINT, drift_distance REAL) AS $$
BEGIN
    RETURN QUERY
    SELECT
        a.atom_id,
        ST_Distance(
            a.spatial_key,
            ST_Centroid(ST_Collect(neighbor.spatial_key))
        )::REAL AS drift
    FROM atom a
    CROSS JOIN LATERAL (
        SELECT spatial_key
        FROM atom
        WHERE canonical_text = a.canonical_text
          AND atom_id != a.atom_id
        LIMIT 100
    ) neighbor
    GROUP BY a.atom_id, a.spatial_key
    HAVING ST_Distance(a.spatial_key, ST_Centroid(ST_Collect(neighbor.spatial_key))) > 1.0
    ORDER BY drift DESC;
END;
$$ LANGUAGE plpgsql;
```

**Action**: Recompute spatial position for drifted atoms.

---

## Automated Index Management

```sql
-- Detect missing indexes
CREATE FUNCTION suggest_indexes()
RETURNS TABLE(suggestion TEXT) AS $$
BEGIN
    RETURN QUERY

    -- High-cardinality metadata keys
    SELECT
        'CREATE INDEX idx_metadata_' || key || ' ON atom ((metadata->>''' || key || '''))'
    FROM (
        SELECT DISTINCT jsonb_object_keys(metadata) AS key
        FROM atom
    ) keys
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE indexdef LIKE '%metadata%' || key || '%'
    );

END;
$$ LANGUAGE plpgsql;
```

---

## Self-Healing

Automatically fix common issues:

```sql
CREATE FUNCTION ooda_self_heal()
RETURNS VOID AS $$
BEGIN
    -- Vacuum bloated tables
    EXECUTE 'VACUUM ANALYZE atom';

    -- Rebuild fragmented indexes
    EXECUTE 'REINDEX TABLE atom';

    -- Prune weak synapses
    DELETE FROM atom_relation WHERE weight < 0.01;

    -- Update statistics
    EXECUTE 'ANALYZE';

END;
$$ LANGUAGE plpgsql;
```

---

## Scheduled OODA Loop

```sql
-- Using pg_cron extension
SELECT cron.schedule('ooda-hourly', '0 * * * *', 'SELECT ooda_observe()');
SELECT cron.schedule('hebbian-daily', '0 0 * * *', 'SELECT hebbian_reinforcement()');
SELECT cron.schedule('self-heal-weekly', '0 0 * * 0', 'SELECT ooda_self_heal()');
```

---

## Metrics to Track

```sql
CREATE TABLE ooda_metrics (
    metric_id BIGSERIAL PRIMARY KEY,
    metric_name TEXT,
    metric_value REAL,
    recorded_at TIMESTAMPTZ DEFAULT now()
);

-- Sample metrics
INSERT INTO ooda_metrics (metric_name, metric_value)
SELECT 'avg_query_time_ms', AVG(mean_exec_time) FROM pg_stat_statements;

INSERT INTO ooda_metrics (metric_name, metric_value)
SELECT 'total_atoms', COUNT(*) FROM atom;

INSERT INTO ooda_metrics (metric_name, metric_value)
SELECT 'spatial_index_size_mb', pg_relation_size('idx_atom_spatial') / 1024.0 / 1024.0;
```

---

## Provenance Tracking

Every optimization logged with full context:

```sql
CREATE TABLE ooda_provenance (
    provenance_id BIGSERIAL PRIMARY KEY,
    observation JSONB,
    orientation JSONB,
    decision TEXT,
    action_result TEXT,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- Example entry
INSERT INTO ooda_provenance (observation, orientation, decision, action_result)
VALUES (
    '{"issue": "slow_query", "metric": 150.5}',
    '{"root_cause": "missing_index", "recommendation": "CREATE INDEX"}',
    'CREATE INDEX idx_atom_metadata_model ON atom ((metadata->>''model_name''))',
    'SUCCESS'
);
```

---

## Use Cases

**Autonomous databases**: System tunes itself without DBA intervention.

**Continuous improvement**: Performance improves over time as patterns emerge.

**Anomaly response**: Detect and mitigate performance degradation automatically.

**Knowledge refinement**: Prune weak synapses, strengthen valuable connections.

---

## Safety Guardrails

**Never auto-execute**:
- DROP TABLE/INDEX
- ALTER TABLE (schema changes)
- DELETE without WHERE (mass deletion)

**Always log**:
- All actions in ooda_audit_log
- All metrics in ooda_metrics
- Full provenance chain

**Approval queue**:
- Dangerous operations flagged for manual review
- Notifications via pg_notify

---

**See Also**: [07-COGNITIVE-PHYSICS.md](07-COGNITIVE-PHYSICS.md), [02-ARCHITECTURE.md](02-ARCHITECTURE.md)
