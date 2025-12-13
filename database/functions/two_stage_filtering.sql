-- Two-stage spatial filtering: coarse GiST → fine Fréchet/semantic
-- Optimizes trajectory and pattern matching with early rejection

-- Stage 1: GiST R-Tree coarse filtering (fast bounding box check)
-- Stage 2: Fine-grained distance calculation (Fréchet, Hausdorff, DTW)

CREATE OR REPLACE FUNCTION two_stage_trajectory_search(
    query_geom GEOMETRY,
    radius DOUBLE PRECISION DEFAULT 1.0,
    k INTEGER DEFAULT 10,
    fine_threshold DOUBLE PRECISION DEFAULT 0.5
) RETURNS TABLE (
    atom_id BYTEA,
    coarse_distance DOUBLE PRECISION,
    fine_distance DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    WITH coarse_candidates AS (
        -- Stage 1: GiST index scan (fast)
        SELECT 
            a.atom_id,
            ST_Distance(a.geom, query_geom) as coarse_dist
        FROM atom a
        WHERE a.atom_class = 1  -- Compositions only
          AND ST_DWithin(a.geom, query_geom, radius)
        ORDER BY a.geom <-> query_geom
        LIMIT k * 5  -- Get 5x more candidates for filtering
    ),
    fine_filtered AS (
        -- Stage 2: Fréchet distance (expensive but accurate)
        SELECT 
            cc.atom_id,
            cc.coarse_dist,
            ST_FrechetDistance(
                (SELECT geom FROM atom WHERE atom.atom_id = cc.atom_id),
                query_geom
            ) as frechet_dist
        FROM coarse_candidates cc
        WHERE ST_FrechetDistance(
            (SELECT geom FROM atom WHERE atom.atom_id = cc.atom_id),
            query_geom
        ) <= fine_threshold
    )
    SELECT 
        ff.atom_id,
        ff.coarse_dist,
        ff.frechet_dist
    FROM fine_filtered ff
    ORDER BY ff.frechet_dist
    LIMIT k;
END;
$$ LANGUAGE plpgsql;

-- Optimized pattern search with early rejection
CREATE OR REPLACE FUNCTION pattern_search_optimized(
    query_pattern GEOMETRY,
    min_support INTEGER DEFAULT 3,
    similarity_threshold DOUBLE PRECISION DEFAULT 0.8
) RETURNS TABLE (
    pattern_id BYTEA,
    support_count INTEGER,
    avg_similarity DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    WITH 
    -- Stage 1: Coarse filter by bounding box overlap
    coarse_matches AS (
        SELECT DISTINCT a.atom_id
        FROM atom a
        WHERE a.atom_class = 1
          AND ST_Intersects(
              ST_Envelope(a.geom),
              ST_Envelope(query_pattern)
          )
    ),
    -- Stage 2: Fine similarity calculation
    fine_matches AS (
        SELECT 
            cm.atom_id,
            1.0 - (ST_HausdorffDistance(
                (SELECT geom FROM atom WHERE atom.atom_id = cm.atom_id),
                query_pattern
            ) / ST_Length(query_pattern)) as similarity
        FROM coarse_matches cm
        WHERE 1.0 - (ST_HausdorffDistance(
                (SELECT geom FROM atom WHERE atom.atom_id = cm.atom_id),
                query_pattern
            ) / ST_Length(query_pattern)) >= similarity_threshold
    ),
    -- Group patterns
    pattern_groups AS (
        SELECT 
            fm.atom_id,
            COUNT(*) as support,
            AVG(fm.similarity) as avg_sim
        FROM fine_matches fm
        GROUP BY fm.atom_id
        HAVING COUNT(*) >= min_support
    )
    SELECT 
        pg.atom_id,
        pg.support::INTEGER,
        pg.avg_sim
    FROM pattern_groups pg
    ORDER BY pg.support DESC, pg.avg_sim DESC;
END;
$$ LANGUAGE plpgsql;

-- Stage metrics for monitoring performance
CREATE TABLE IF NOT EXISTS filter_stage_metrics (
    metric_id SERIAL PRIMARY KEY,
    query_type VARCHAR(50),
    coarse_candidates INTEGER,
    fine_matches INTEGER,
    rejection_rate DOUBLE PRECISION,
    coarse_time_ms DOUBLE PRECISION,
    fine_time_ms DOUBLE PRECISION,
    total_time_ms DOUBLE PRECISION,
    recorded_at TIMESTAMPTZ DEFAULT now()
);

-- Log filtering performance
CREATE OR REPLACE FUNCTION log_filter_metrics(
    p_query_type VARCHAR(50),
    p_coarse_count INTEGER,
    p_fine_count INTEGER,
    p_coarse_time DOUBLE PRECISION,
    p_fine_time DOUBLE PRECISION
) RETURNS VOID AS $$
BEGIN
    INSERT INTO filter_stage_metrics (
        query_type,
        coarse_candidates,
        fine_matches,
        rejection_rate,
        coarse_time_ms,
        fine_time_ms,
        total_time_ms
    ) VALUES (
        p_query_type,
        p_coarse_count,
        p_fine_count,
        1.0 - (p_fine_count::DOUBLE PRECISION / NULLIF(p_coarse_count, 0)),
        p_coarse_time,
        p_fine_time,
        p_coarse_time + p_fine_time
    );
END;
$$ LANGUAGE plpgsql;

-- View: Filter performance statistics
CREATE OR REPLACE VIEW v_filter_performance AS
SELECT
    query_type,
    COUNT(*) as query_count,
    AVG(rejection_rate) * 100 as avg_rejection_pct,
    AVG(coarse_time_ms) as avg_coarse_ms,
    AVG(fine_time_ms) as avg_fine_ms,
    AVG(total_time_ms) as avg_total_ms,
    AVG(coarse_candidates) as avg_coarse_candidates,
    AVG(fine_matches) as avg_fine_matches
FROM filter_stage_metrics
WHERE recorded_at > now() - interval '1 hour'
GROUP BY query_type
ORDER BY avg_total_ms DESC;

COMMENT ON FUNCTION two_stage_trajectory_search IS 'Two-stage filtering: GiST coarse filter → Fréchet fine distance';
COMMENT ON FUNCTION pattern_search_optimized IS 'Pattern mining with bounding box early rejection';
COMMENT ON TABLE filter_stage_metrics IS 'Performance metrics for two-stage filtering';
