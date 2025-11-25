-- ============================================================================
-- A* Pathfinding in Semantic Space
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Find optimal semantic path between concepts through relation graph
-- Use: Reasoning chains, concept navigation, provenance tracing
-- ============================================================================

CREATE OR REPLACE FUNCTION astar_semantic_path(
    p_start_atom_id BIGINT,
    p_goal_atom_id BIGINT,
    p_max_depth INTEGER DEFAULT 10
)
RETURNS TABLE(
    step INTEGER,
    atom_id BIGINT,
    canonical_text TEXT,
    relation_type TEXT,
    g_cost REAL,  -- Actual cost from start
    h_cost REAL,  -- Heuristic cost to goal
    f_cost REAL   -- Total estimated cost
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_goal_position GEOMETRY;
    v_current_id BIGINT;
    v_current_g REAL;
    v_current_f REAL;
    v_neighbor_id BIGINT;
    v_neighbor_g REAL;
    v_neighbor_h REAL;
    v_neighbor_f REAL;
    v_edge_weight REAL;
    v_path_found BOOLEAN := FALSE;
BEGIN
    -- Get goal position for heuristic
    SELECT spatial_key INTO v_goal_position
    FROM atom
    WHERE atom_id = p_goal_atom_id;
    
    IF v_goal_position IS NULL THEN
        RAISE EXCEPTION 'Goal atom % has no spatial position', p_goal_atom_id;
    END IF;
    
    -- Create temporary tables for A* algorithm
    CREATE TEMP TABLE IF NOT EXISTS astar_open_set (
        atom_id BIGINT PRIMARY KEY,
        g_cost REAL,
        f_cost REAL,
        parent_id BIGINT
    ) ON COMMIT DROP;
    
    CREATE TEMP TABLE IF NOT EXISTS astar_closed_set (
        atom_id BIGINT PRIMARY KEY
    ) ON COMMIT DROP;
    
    -- Initialize with start node
    INSERT INTO astar_open_set (atom_id, g_cost, f_cost, parent_id)
    SELECT 
        p_start_atom_id,
        0,
        ST_Distance(
            spatial_key,
            v_goal_position
        ),
        NULL
    FROM atom
    WHERE atom_id = p_start_atom_id;
    
    -- A* main loop
    FOR i IN 1..p_max_depth LOOP
        -- Get node with lowest f_cost
        SELECT atom_id, g_cost, f_cost
        INTO v_current_id, v_current_g, v_current_f
        FROM astar_open_set
        ORDER BY f_cost ASC
        LIMIT 1;
        
        EXIT WHEN v_current_id IS NULL;
        
        -- Check if goal reached
        IF v_current_id = p_goal_atom_id THEN
            v_path_found := TRUE;
            EXIT;
        END IF;
        
        -- Move current to closed set
        DELETE FROM astar_open_set WHERE atom_id = v_current_id;
        INSERT INTO astar_closed_set (atom_id) VALUES (v_current_id);
        
        -- Explore neighbors via relations
        FOR v_neighbor_id, v_edge_weight IN
            SELECT 
                ar.target_atom_id,
                (1.0 - ar.weight) as cost  -- Higher weight = lower cost
            FROM atom_relation ar
            WHERE ar.source_atom_id = v_current_id
              AND NOT EXISTS (
                  SELECT 1 FROM astar_closed_set 
                  WHERE atom_id = ar.target_atom_id
              )
        LOOP
            v_neighbor_g := v_current_g + v_edge_weight;
            
            -- Heuristic: Euclidean distance to goal
            SELECT ST_Distance(spatial_key, v_goal_position)
            INTO v_neighbor_h
            FROM atom
            WHERE atom_id = v_neighbor_id;
            
            v_neighbor_f := v_neighbor_g + v_neighbor_h;
            
            -- Add or update in open set
            INSERT INTO astar_open_set (atom_id, g_cost, f_cost, parent_id)
            VALUES (v_neighbor_id, v_neighbor_g, v_neighbor_f, v_current_id)
            ON CONFLICT (atom_id) DO UPDATE
            SET g_cost = EXCLUDED.g_cost,
                f_cost = EXCLUDED.f_cost,
                parent_id = EXCLUDED.parent_id
            WHERE astar_open_set.g_cost > EXCLUDED.g_cost;
        END LOOP;
    END LOOP;
    
    -- Reconstruct path if found
    IF v_path_found THEN
        RETURN QUERY
        WITH RECURSIVE path AS (
            SELECT 
                1 as step,
                o.atom_id,
                o.g_cost,
                0.0::REAL as h_cost,
                o.f_cost,
                o.parent_id
            FROM astar_open_set o
            WHERE o.atom_id = p_goal_atom_id
            
            UNION ALL
            
            SELECT 
                p.step + 1,
                o.atom_id,
                o.g_cost,
                0.0::REAL,
                o.f_cost,
                o.parent_id
            FROM path p
            JOIN astar_open_set o ON o.atom_id = p.parent_id
        )
        SELECT 
            p.step,
            p.atom_id,
            a.canonical_text,
            COALESCE(rt.canonical_text, 'start') as relation_type,
            p.g_cost,
            p.h_cost,
            p.f_cost
        FROM path p
        JOIN atom a ON a.atom_id = p.atom_id
        LEFT JOIN atom_relation ar ON ar.target_atom_id = p.atom_id
        LEFT JOIN atom rt ON rt.atom_id = ar.relation_type_id
        ORDER BY p.step DESC;
    END IF;
    
END;
$$;

COMMENT ON FUNCTION astar_semantic_path(BIGINT, BIGINT, INTEGER) IS 
'A* pathfinding through semantic relation graph.
Finds optimal path from start concept to goal concept using:
  - g(n): Actual cost via relation weights
  - h(n): Heuristic via spatial distance
  - f(n) = g(n) + h(n): Total estimated cost
Returns ordered path with costs.';
