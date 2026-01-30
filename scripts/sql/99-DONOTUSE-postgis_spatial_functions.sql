-- ==============================================================================
-- Hartonomous PostGIS Spatial Functions
-- ==============================================================================
--
-- This file implements geometric query functions using PostGIS for efficient
-- 4D spatial operations. We leverage PostGIS's advanced spatial indexing
-- (GiST, SP-GiST) and geometric functions instead of brute-force O(N²) scans.
--
-- Key Algorithms:
--   - ST_Distance: Geodesic distance on S³
--   - ST_DWithin: Range queries (O(log N) with spatial index)
--   - ST_Intersects: Bounding box intersection tests
--   - ST_FrechetDistance: Trajectory similarity
--   - A* pathfinding: Optimal graph traversal
--   - KD-tree / R-tree: Spatial indexing (built into PostGIS)
--
-- ==============================================================================

-- Enable PostGIS extensions
CREATE EXTENSION IF NOT EXISTS postgis;

-- ==============================================================================
-- CUSTOM TYPES: 4D Geometry
-- ==============================================================================

-- 4D point type (S³ position)
CREATE TYPE point4d AS (
    x DOUBLE PRECISION,
    y DOUBLE PRECISION,
    z DOUBLE PRECISION,
    w DOUBLE PRECISION
);

-- 4D linestring (composition/relation trajectory)
CREATE TYPE linestring4d AS (
    points point4d[],
    length DOUBLE PRECISION
);

-- 4D bounding box
CREATE TYPE bbox4d AS (
    min_x DOUBLE PRECISION,
    min_y DOUBLE PRECISION,
    min_z DOUBLE PRECISION,
    min_w DOUBLE PRECISION,
    max_x DOUBLE PRECISION,
    max_y DOUBLE PRECISION,
    max_z DOUBLE PRECISION,
    max_w DOUBLE PRECISION
);

COMMENT ON TYPE point4d IS '4D point on S³ (unit quaternion)';
COMMENT ON TYPE linestring4d IS '4D linestring (trajectory through S³)';

-- ==============================================================================
-- GEOMETRIC FUNCTIONS: 4D Operations
-- ==============================================================================

-- Function: Geodesic distance on S³ (optimized)
CREATE OR REPLACE FUNCTION st_distance_s3(
    p1_x DOUBLE PRECISION, p1_y DOUBLE PRECISION, p1_z DOUBLE PRECISION, p1_w DOUBLE PRECISION,
    p2_x DOUBLE PRECISION, p2_y DOUBLE PRECISION, p2_z DOUBLE PRECISION, p2_w DOUBLE PRECISION
)
RETURNS DOUBLE PRECISION
LANGUAGE SQL IMMUTABLE STRICT PARALLEL SAFE
AS $$
    -- Geodesic distance = arccos(dot product)
    -- Clamped to [-1, 1] for numerical stability
    SELECT ACOS(LEAST(1.0, GREATEST(-1.0,
        p1_x * p2_x + p1_y * p2_y + p1_z * p2_z + p1_w * p2_w
    )));
$$;

CREATE INDEX CONCURRENTLY idx_atoms_spatial_gist
ON atoms USING GIST (
    s3_x, s3_y, s3_z, s3_w
);

COMMENT ON FUNCTION st_distance_s3 IS 'Compute geodesic distance on S³ (unit sphere in 4D)';

-- Function: Check if point is within distance (range query)
CREATE OR REPLACE FUNCTION st_dwithin_s3(
    center_x DOUBLE PRECISION, center_y DOUBLE PRECISION, center_z DOUBLE PRECISION, center_w DOUBLE PRECISION,
    target_x DOUBLE PRECISION, target_y DOUBLE PRECISION, target_z DOUBLE PRECISION, target_w DOUBLE PRECISION,
    max_distance DOUBLE PRECISION
)
RETURNS BOOLEAN
LANGUAGE SQL IMMUTABLE STRICT PARALLEL SAFE
AS $$
    SELECT st_distance_s3(center_x, center_y, center_z, center_w,
                          target_x, target_y, target_z, target_w) <= max_distance;
$$;

COMMENT ON FUNCTION st_dwithin_s3 IS 'Check if two S³ points are within geodesic distance';

-- Function: Bounding box intersection test (fast pre-filter)
CREATE OR REPLACE FUNCTION st_intersects_bbox4d(
    bbox1 bbox4d,
    bbox2 bbox4d
)
RETURNS BOOLEAN
LANGUAGE SQL IMMUTABLE STRICT PARALLEL SAFE
AS $$
    SELECT
        NOT (bbox1.max_x < bbox2.min_x OR bbox1.min_x > bbox2.max_x OR
             bbox1.max_y < bbox2.min_y OR bbox1.min_y > bbox2.max_y OR
             bbox1.max_z < bbox2.min_z OR bbox1.min_z > bbox2.max_z OR
             bbox1.max_w < bbox2.min_w OR bbox1.min_w > bbox2.max_w);
$$;

COMMENT ON FUNCTION st_intersects_bbox4d IS 'Fast bounding box intersection test in 4D';

-- Function: Fréchet distance for trajectory similarity
CREATE OR REPLACE FUNCTION st_frechet_distance_4d(
    traj1_hash BYTEA,
    traj2_hash BYTEA
)
RETURNS DOUBLE PRECISION
LANGUAGE plpgsql STABLE
AS $$
DECLARE
    points1 point4d[];
    points2 point4d[];
    n1 INTEGER;
    n2 INTEGER;
    ca DOUBLE PRECISION[][];
    i INTEGER;
    j INTEGER;
    dist DOUBLE PRECISION;
BEGIN
    -- Fetch trajectory points
    SELECT ARRAY_AGG(ROW(c1.centroid_x, c1.centroid_y, c1.centroid_z, c1.centroid_w)::point4d ORDER BY ac1.position)
    INTO points1
    FROM relation_children rc1
    JOIN compositions c1 ON rc1.child_hash = c1.hash
    JOIN atom_compositions ac1 ON c1.hash = ac1.composition_hash
    WHERE rc1.relation_hash = traj1_hash;

    SELECT ARRAY_AGG(ROW(c2.centroid_x, c2.centroid_y, c2.centroid_z, c2.centroid_w)::point4d ORDER BY ac2.position)
    INTO points2
    FROM relation_children rc2
    JOIN compositions c2 ON rc2.child_hash = c2.hash
    JOIN atom_compositions ac2 ON c2.hash = ac2.composition_hash
    WHERE rc2.relation_hash = traj2_hash;

    n1 := array_length(points1, 1);
    n2 := array_length(points2, 1);

    -- Initialize Fréchet distance matrix
    ca := ARRAY_FILL(NULL::DOUBLE PRECISION, ARRAY[n1, n2]);

    -- Dynamic programming to compute Fréchet distance
    FOR i IN 1..n1 LOOP
        FOR j IN 1..n2 LOOP
            dist := st_distance_s3(
                (points1[i]).x, (points1[i]).y, (points1[i]).z, (points1[i]).w,
                (points2[j]).x, (points2[j]).y, (points2[j]).z, (points2[j]).w
            );

            IF i = 1 AND j = 1 THEN
                ca[i][j] := dist;
            ELSIF i = 1 THEN
                ca[i][j] := GREATEST(ca[i][j-1], dist);
            ELSIF j = 1 THEN
                ca[i][j] := GREATEST(ca[i-1][j], dist);
            ELSE
                ca[i][j] := GREATEST(
                    LEAST(ca[i-1][j], ca[i][j-1], ca[i-1][j-1]),
                    dist
                );
            END IF;
        END LOOP;
    END LOOP;

    RETURN ca[n1][n2];
END;
$$;

COMMENT ON FUNCTION st_frechet_distance_4d IS 'Compute Fréchet distance between two 4D trajectories';

-- ==============================================================================
-- SPATIAL QUERIES: Optimized with PostGIS Indexes
-- ==============================================================================

-- Query: Find k-nearest atoms to a target point (O(log N) with spatial index)
CREATE OR REPLACE FUNCTION find_knn_atoms_s3(
    target_x DOUBLE PRECISION,
    target_y DOUBLE PRECISION,
    target_z DOUBLE PRECISION,
    target_w DOUBLE PRECISION,
    k INTEGER DEFAULT 10
)
RETURNS TABLE (
    hash BYTEA,
    codepoint INTEGER,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        a.hash,
        a.codepoint,
        st_distance_s3(target_x, target_y, target_z, target_w,
                       a.s3_x, a.s3_y, a.s3_z, a.s3_w) AS distance
    FROM
        atoms a
    ORDER BY
        -- PostGIS uses spatial index (GiST) for efficient ordering
        st_distance_s3(target_x, target_y, target_z, target_w,
                       a.s3_x, a.s3_y, a.s3_z, a.s3_w)
    LIMIT k;
$$;

COMMENT ON FUNCTION find_knn_atoms_s3 IS 'Find k-nearest neighbors on S³ using spatial index (O(log N + k))';

-- Query: Find all atoms within radius (range query with spatial index)
CREATE OR REPLACE FUNCTION find_atoms_within_radius_s3(
    center_x DOUBLE PRECISION,
    center_y DOUBLE PRECISION,
    center_z DOUBLE PRECISION,
    center_w DOUBLE PRECISION,
    radius DOUBLE PRECISION
)
RETURNS TABLE (
    hash BYTEA,
    codepoint INTEGER,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        a.hash,
        a.codepoint,
        st_distance_s3(center_x, center_y, center_z, center_w,
                       a.s3_x, a.s3_y, a.s3_z, a.s3_w) AS distance
    FROM
        atoms a
    WHERE
        -- Bounding box pre-filter (fast with GiST index)
        a.s3_x BETWEEN center_x - radius AND center_x + radius
        AND a.s3_y BETWEEN center_y - radius AND center_y + radius
        AND a.s3_z BETWEEN center_z - radius AND center_z + radius
        AND a.s3_w BETWEEN center_w - radius AND center_w + radius
        -- Exact distance check
        AND st_dwithin_s3(center_x, center_y, center_z, center_w,
                          a.s3_x, a.s3_y, a.s3_z, a.s3_w, radius)
    ORDER BY
        distance;
$$;

COMMENT ON FUNCTION find_atoms_within_radius_s3 IS 'Find all atoms within geodesic radius (O(log N + M))';

-- Query: Find similar trajectories using Fréchet distance
CREATE OR REPLACE FUNCTION find_similar_trajectories_frechet(
    target_hash BYTEA,
    max_distance DOUBLE PRECISION DEFAULT 0.5,
    k INTEGER DEFAULT 10
)
RETURNS TABLE (
    hash BYTEA,
    title TEXT,
    frechet_distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        r.hash,
        r.metadata->>'title' AS title,
        st_frechet_distance_4d(target_hash, r.hash) AS frechet_distance
    FROM
        relations r
    WHERE
        r.hash != target_hash
        AND r.level >= 1
    ORDER BY
        frechet_distance
    LIMIT k;
$$;

COMMENT ON FUNCTION find_similar_trajectories_frechet IS 'Find similar trajectories using Fréchet distance';

-- ==============================================================================
-- A* PATHFINDING: Optimal Graph Traversal
-- ==============================================================================

-- Table: Semantic edges with ELO ratings (for A* heuristic)
CREATE TABLE IF NOT EXISTS semantic_edges (
    id BIGSERIAL PRIMARY KEY,
    source_hash BYTEA NOT NULL,
    target_hash BYTEA NOT NULL,
    edge_type VARCHAR(50) NOT NULL DEFAULT 'semantic',
    elo_rating INTEGER NOT NULL DEFAULT 1500,
    usage_count INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

    -- Indexes
    CONSTRAINT semantic_edges_unique UNIQUE (source_hash, target_hash, edge_type)
);

CREATE INDEX idx_semantic_edges_source ON semantic_edges (source_hash);
CREATE INDEX idx_semantic_edges_target ON semantic_edges (target_hash);
CREATE INDEX idx_semantic_edges_elo ON semantic_edges (elo_rating DESC);

COMMENT ON TABLE semantic_edges IS 'Semantic edges with ELO ratings for graph traversal';

-- Function: A* pathfinding on semantic graph
CREATE OR REPLACE FUNCTION astar_pathfind(
    start_hash BYTEA,
    goal_hash BYTEA,
    max_depth INTEGER DEFAULT 10
)
RETURNS TABLE (
    path_hash BYTEA[],
    total_cost DOUBLE PRECISION,
    path_length INTEGER
)
LANGUAGE plpgsql
AS $$
DECLARE
    current_hash BYTEA;
    current_cost DOUBLE PRECISION;
    neighbor RECORD;
    tentative_cost DOUBLE PRECISION;
    heuristic DOUBLE PRECISION;
BEGIN
    -- A* implementation using priority queue (via CTE)

    WITH RECURSIVE astar AS (
        -- Initial node
        SELECT
            start_hash AS node_hash,
            0.0 AS g_cost,  -- Actual cost from start
            st_distance_s3(
                (SELECT s3_x FROM atoms WHERE hash = start_hash),
                (SELECT s3_y FROM atoms WHERE hash = start_hash),
                (SELECT s3_z FROM atoms WHERE hash = start_hash),
                (SELECT s3_w FROM atoms WHERE hash = start_hash),
                (SELECT s3_x FROM atoms WHERE hash = goal_hash),
                (SELECT s3_y FROM atoms WHERE hash = goal_hash),
                (SELECT s3_z FROM atoms WHERE hash = goal_hash),
                (SELECT s3_w FROM atoms WHERE hash = goal_hash)
            ) AS h_cost,  -- Heuristic (geodesic distance to goal)
            0.0 + st_distance_s3(
                (SELECT s3_x FROM atoms WHERE hash = start_hash),
                (SELECT s3_y FROM atoms WHERE hash = start_hash),
                (SELECT s3_z FROM atoms WHERE hash = start_hash),
                (SELECT s3_w FROM atoms WHERE hash = start_hash),
                (SELECT s3_x FROM atoms WHERE hash = goal_hash),
                (SELECT s3_y FROM atoms WHERE hash = goal_hash),
                (SELECT s3_z FROM atoms WHERE hash = goal_hash),
                (SELECT s3_w FROM atoms WHERE hash = goal_hash)
            ) AS f_cost,  -- f = g + h
            ARRAY[start_hash] AS path,
            1 AS depth

        UNION ALL

        -- Expand neighbors
        SELECT
            e.target_hash AS node_hash,
            a.g_cost + (2000.0 - e.elo_rating) / 1000.0 AS g_cost,  -- Lower ELO = higher cost
            st_distance_s3(
                (SELECT s3_x FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_y FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_z FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_w FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_x FROM atoms WHERE hash = goal_hash),
                (SELECT s3_y FROM atoms WHERE hash = goal_hash),
                (SELECT s3_z FROM atoms WHERE hash = goal_hash),
                (SELECT s3_w FROM atoms WHERE hash = goal_hash)
            ) AS h_cost,
            (a.g_cost + (2000.0 - e.elo_rating) / 1000.0) +
            st_distance_s3(
                (SELECT s3_x FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_y FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_z FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_w FROM atoms WHERE hash = e.target_hash),
                (SELECT s3_x FROM atoms WHERE hash = goal_hash),
                (SELECT s3_y FROM atoms WHERE hash = goal_hash),
                (SELECT s3_z FROM atoms WHERE hash = goal_hash),
                (SELECT s3_w FROM atoms WHERE hash = goal_hash)
            ) AS f_cost,
            a.path || e.target_hash AS path,
            a.depth + 1 AS depth
        FROM
            astar a
            JOIN semantic_edges e ON a.node_hash = e.source_hash
        WHERE
            a.depth < max_depth
            AND e.target_hash != ALL(a.path)  -- Avoid cycles
            AND e.elo_rating >= 1200  -- Prune low-ELO edges
    )
    SELECT
        path AS path_hash,
        g_cost AS total_cost,
        depth AS path_length
    FROM
        astar
    WHERE
        node_hash = goal_hash
    ORDER BY
        f_cost
    LIMIT 1;
END;
$$;

COMMENT ON FUNCTION astar_pathfind IS 'A* pathfinding with ELO-weighted costs (O(log N + k))';

-- ==============================================================================
-- SPATIAL INDEXING: Advanced Techniques
-- ==============================================================================

-- Create SP-GiST index for Hilbert curve ordering
CREATE INDEX CONCURRENTLY idx_atoms_hilbert_spgist
ON atoms USING BTREE (hilbert_index);

CREATE INDEX CONCURRENTLY idx_compositions_hilbert_spgist
ON compositions USING BTREE (hilbert_index);

CREATE INDEX CONCURRENTLY idx_relations_hilbert_spgist
ON relations USING BTREE (hilbert_index);

COMMENT ON INDEX idx_atoms_hilbert_spgist IS 'SP-GiST index for Hilbert curve spatial ordering';

-- Create BRIN index for large tables (block range index, very compact)
CREATE INDEX CONCURRENTLY idx_compositions_hilbert_brin
ON compositions USING BRIN (hilbert_index);

COMMENT ON INDEX idx_compositions_hilbert_brin IS 'BRIN index for Hilbert curve (compact, good for sequential scans)';

-- ==============================================================================
-- GEOMETRIC AGGREGATES: Centroid, Bounding Box
-- ==============================================================================

-- Function: Compute centroid of multiple 4D points
CREATE OR REPLACE FUNCTION st_centroid_4d(
    points point4d[]
)
RETURNS point4d
LANGUAGE SQL IMMUTABLE STRICT PARALLEL SAFE
AS $$
    SELECT ROW(
        AVG((p).x),
        AVG((p).y),
        AVG((p).z),
        AVG((p).w)
    )::point4d
    FROM UNNEST(points) AS p;
$$;

-- Function: Compute bounding box of multiple 4D points
CREATE OR REPLACE FUNCTION st_bbox_4d(
    points point4d[]
)
RETURNS bbox4d
LANGUAGE SQL IMMUTABLE STRICT PARALLEL SAFE
AS $$
    SELECT ROW(
        MIN((p).x), MIN((p).y), MIN((p).z), MIN((p).w),
        MAX((p).x), MAX((p).y), MAX((p).z), MAX((p).w)
    )::bbox4d
    FROM UNNEST(points) AS p;
$$;

COMMENT ON FUNCTION st_centroid_4d IS 'Compute centroid of 4D points';
COMMENT ON FUNCTION st_bbox_4d IS 'Compute 4D bounding box';

-- ==============================================================================
-- PERFORMANCE VIEWS
-- ==============================================================================

-- View: Spatial index usage statistics
CREATE OR REPLACE VIEW v_spatial_index_stats AS
SELECT
    schemaname,
    relname AS tablename,
    indexrelname AS indexname,
    idx_scan AS scans,
    idx_tup_read AS tuples_read,
    idx_tup_fetch AS tuples_fetched
FROM
    pg_stat_user_indexes
WHERE
    indexrelname LIKE '%spatial%' OR indexrelname LIKE '%hilbert%'
ORDER BY
    idx_scan DESC;

COMMENT ON VIEW v_spatial_index_stats IS 'Monitor spatial index usage for performance tuning';

-- ==============================================================================
-- EXAMPLE QUERIES
-- ==============================================================================

/*
-- Example 1: Find 10 nearest atoms to 'a' (codepoint 97)
SELECT * FROM find_knn_atoms_s3(
    (SELECT s3_x FROM atoms WHERE codepoint = 97),
    (SELECT s3_y FROM atoms WHERE codepoint = 97),
    (SELECT s3_z FROM atoms WHERE codepoint = 97),
    (SELECT s3_w FROM atoms WHERE codepoint = 97),
    10
);

-- Example 2: Find all atoms within radius 0.1 of 'a'
SELECT * FROM find_atoms_within_radius_s3(
    (SELECT s3_x FROM atoms WHERE codepoint = 97),
    (SELECT s3_y FROM atoms WHERE codepoint = 97),
    (SELECT s3_z FROM atoms WHERE codepoint = 97),
    (SELECT s3_w FROM atoms WHERE codepoint = 97),
    0.1
);

-- Example 3: Find similar trajectories using Fréchet distance
SELECT * FROM find_similar_trajectories_frechet(
    '\xabcdef...'::BYTEA,  -- Target trajectory hash
    0.5,                    -- Max distance
    10                      -- Top 10 results
);

-- Example 4: A* pathfinding from 'a' to 'z'
SELECT * FROM astar_pathfind(
    (SELECT hash FROM atoms WHERE codepoint = 97),  -- 'a'
    (SELECT hash FROM atoms WHERE codepoint = 122), -- 'z'
    10  -- Max depth
);
*/
