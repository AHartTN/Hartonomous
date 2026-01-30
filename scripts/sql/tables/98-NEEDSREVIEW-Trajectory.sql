-- ==============================================================================
-- TABLE: trajectories
-- ==============================================================================
--
-- Stores precomputed trajectory statistics for efficient querying.
-- A trajectory is the 4D path through space traced by a relation.
--
-- Use Cases:
--   - Find documents with similar "shape" in 4D space
--   - Detect plagiarism via trajectory matching
--   - Cluster documents by geometric similarity
--   - Visualize document structure in 4D/3D
--
CREATE TABLE trajectories (
    -- Foreign key to relation
    relation_hash BYTEA PRIMARY KEY REFERENCES relations(hash) ON DELETE CASCADE,

    -- Trajectory statistics
    total_distance DOUBLE PRECISION NOT NULL CHECK (total_distance >= 0),
    avg_step_distance DOUBLE PRECISION NOT NULL CHECK (avg_step_distance >= 0),
    max_step_distance DOUBLE PRECISION NOT NULL CHECK (max_step_distance >= 0),
    tortuosity DOUBLE PRECISION NOT NULL CHECK (tortuosity >= 1.0), -- total_distance / straight_line_distance

    -- Directional statistics (normalized)
    primary_direction_x DOUBLE PRECISION NOT NULL,
    primary_direction_y DOUBLE PRECISION NOT NULL,
    primary_direction_z DOUBLE PRECISION NOT NULL,
    primary_direction_w DOUBLE PRECISION NOT NULL,

    -- Complexity metrics
    fractal_dimension DOUBLE PRECISION, -- Estimated via box-counting
    entropy DOUBLE PRECISION,           -- Shannon entropy of direction changes

    -- Precomputed for visualization
    simplified_trajectory BYTEA,        -- Compressed trajectory (for rendering)

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX idx_trajectories_tortuosity ON trajectories (tortuosity);
CREATE INDEX idx_trajectories_fractal_dim ON trajectories (fractal_dimension);

COMMENT ON TABLE trajectories IS 'Precomputed trajectory statistics for relations';
COMMENT ON COLUMN trajectories.tortuosity IS 'Ratio of trajectory length to straight-line distance (≥1)';
COMMENT ON COLUMN trajectories.fractal_dimension IS 'Estimated fractal dimension via box-counting';