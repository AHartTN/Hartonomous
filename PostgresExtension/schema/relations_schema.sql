-- ==============================================================================
-- Hartonomous Relations Schema - Hierarchical Merkle DAG Extension
-- ==============================================================================
--
-- This schema extends the base Atoms/Compositions system to support hierarchical
-- relationships forming a Merkle DAG (Directed Acyclic Graph) structure.
--
-- Hierarchical Structure:
--   Level 0: Atoms (Unicode codepoints)
--   Level 1: Compositions (n-grams of atoms, e.g., "whale", "the king")
--   Level 2: Relations (n-grams of compositions, e.g., sentences, paragraphs)
--   Level 3+: Higher-order relations (chapters, books, entire documents)
--
-- Example: Moby Dick
--   - "whale" is a Composition (stored ONCE)
--   - "Call me Ishmael" is a Relation of Compositions
--   - Chapter 1 is a Relation of sentence Relations
--   - Moby Dick is a Relation of chapter Relations
--
-- Key Properties:
--   - Universal substrate: Works for ANY digital content (text, code, data)
--   - Content-addressable: SAME CONTENT = SAME HASH at all levels
--   - Modality-agnostic: Unicode is the universal representation
--   - Compression: Byte-pair encoding, run-length encoding naturally emerge
--   - Merkle DAG: Cryptographic integrity at all levels
--
-- ==============================================================================

-- ==============================================================================
-- TABLE: relations
-- ==============================================================================
--
-- Stores sequences of compositions (or other relations) as hierarchical structures.
-- This forms the backbone of the Merkle DAG for representing documents.
--
-- Examples:
--   - Sentence: sequence of word Compositions
--   - Paragraph: sequence of sentence Relations
--   - Chapter: sequence of paragraph Relations
--   - Book: sequence of chapter Relations
--
-- Columns:
--   - hash: BLAKE3 hash of child sequence (PRIMARY KEY)
--   - level: Hierarchy level (1 = composition sequences, 2 = relation sequences, etc.)
--   - length: Number of children in the sequence
--   - centroid_x/y/z/w: Geometric centroid in 4D (trajectory centroid)
--   - hilbert_index: Hilbert index of centroid
--   - parent_type: Type of children ('composition' or 'relation')
--   - metadata: JSONB for flexible metadata (type, title, author, etc.)
--
CREATE TABLE relations (
    -- Primary key: Content hash (BLAKE3 of child sequence)
    hash BYTEA PRIMARY KEY CHECK (octet_length(hash) = 32),

    -- Hierarchy level (1 = sequences of compositions, 2+ = sequences of relations)
    level INTEGER NOT NULL CHECK (level >= 1),

    -- Sequence metadata
    length INTEGER NOT NULL CHECK (length > 0),

    -- Geometric centroid in 4D (trajectory centroid)
    centroid_x DOUBLE PRECISION NOT NULL,
    centroid_y DOUBLE PRECISION NOT NULL,
    centroid_z DOUBLE PRECISION NOT NULL,
    centroid_w DOUBLE PRECISION NOT NULL,

    -- Hilbert index of centroid (for spatial indexing)
    hilbert_index BIGINT NOT NULL,

    -- Bounding box in 4D (for spatial queries)
    bbox_min_x DOUBLE PRECISION NOT NULL,
    bbox_min_y DOUBLE PRECISION NOT NULL,
    bbox_min_z DOUBLE PRECISION NOT NULL,
    bbox_min_w DOUBLE PRECISION NOT NULL,
    bbox_max_x DOUBLE PRECISION NOT NULL,
    bbox_max_y DOUBLE PRECISION NOT NULL,
    bbox_max_z DOUBLE PRECISION NOT NULL,
    bbox_max_w DOUBLE PRECISION NOT NULL,

    -- Trajectory length (geodesic distance through 4D space)
    geometric_length DOUBLE PRECISION NOT NULL CHECK (geometric_length >= 0),

    -- Parent type: 'composition' (level 1) or 'relation' (level 2+)
    parent_type VARCHAR(20) NOT NULL CHECK (parent_type IN ('composition', 'relation')),

    -- Flexible metadata (type, title, author, source, encoding hints, etc.)
    metadata JSONB NOT NULL DEFAULT '{}'::JSONB,

    -- Timestamps
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL
);

-- Indexes for fast queries
CREATE INDEX idx_relations_hilbert ON relations USING BTREE (hilbert_index);
CREATE INDEX idx_relations_level ON relations (level);
CREATE INDEX idx_relations_parent_type ON relations (parent_type);
CREATE INDEX idx_relations_length ON relations (length);

-- GiST index for bounding box queries
CREATE INDEX idx_relations_bbox ON relations USING GIST (
    bbox_min_x, bbox_min_y, bbox_min_z, bbox_min_w,
    bbox_max_x, bbox_max_y, bbox_max_z, bbox_max_w
);

-- GIN index for metadata queries
CREATE INDEX idx_relations_metadata ON relations USING GIN (metadata);

-- Comment
COMMENT ON TABLE relations IS 'Hierarchical sequences forming a Merkle DAG (n-grams of n-grams)';
COMMENT ON COLUMN relations.level IS 'Hierarchy level (1 = compositions, 2+ = relations)';
COMMENT ON COLUMN relations.parent_type IS 'Type of children: composition or relation';
COMMENT ON COLUMN relations.metadata IS 'Flexible JSONB metadata (type, title, encoding, etc.)';

-- ==============================================================================
-- TABLE: relation_children
-- ==============================================================================
--
-- Join table maintaining the sequence order of children within relations.
-- Children can be either compositions (level 1) or other relations (level 2+).
--
-- This is the ONLY place where relation sequences are stored.
--
-- Example: Sentence "Call me Ishmael" (level 1 relation)
--   - child 1: composition "Call"
--   - child 2: composition "me"
--   - child 3: composition "Ishmael"
--
-- Example: Chapter 1 (level 2 relation)
--   - child 1: sentence relation "Call me Ishmael"
--   - child 2: sentence relation "Some years ago..."
--   - ...
--
CREATE TABLE relation_children (
    -- Surrogate key
    id BIGSERIAL PRIMARY KEY,

    -- Parent relation
    relation_hash BYTEA NOT NULL REFERENCES relations(hash) ON DELETE CASCADE,

    -- Child (composition or relation)
    child_hash BYTEA NOT NULL,
    child_type VARCHAR(20) NOT NULL CHECK (child_type IN ('composition', 'relation')),

    -- Position in sequence (0-indexed)
    position INTEGER NOT NULL CHECK (position >= 0),

    -- Unique constraint: each position in a relation is unique
    CONSTRAINT relation_children_unique_position UNIQUE (relation_hash, position)
);

-- Indexes
CREATE INDEX idx_relation_children_relation ON relation_children (relation_hash);
CREATE INDEX idx_relation_children_child ON relation_children (child_hash);
CREATE INDEX idx_relation_children_sequence ON relation_children (relation_hash, position);

-- Foreign key validation trigger (ensure child exists in correct table)
CREATE OR REPLACE FUNCTION validate_relation_child()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.child_type = 'composition' THEN
        -- Verify child exists in compositions table
        IF NOT EXISTS (SELECT 1 FROM compositions WHERE hash = NEW.child_hash) THEN
            RAISE EXCEPTION 'Composition child % does not exist', encode(NEW.child_hash, 'hex');
        END IF;
    ELSIF NEW.child_type = 'relation' THEN
        -- Verify child exists in relations table
        IF NOT EXISTS (SELECT 1 FROM relations WHERE hash = NEW.child_hash) THEN
            RAISE EXCEPTION 'Relation child % does not exist', encode(NEW.child_hash, 'hex');
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_relation_children_validate
BEFORE INSERT OR UPDATE ON relation_children
FOR EACH ROW
EXECUTE FUNCTION validate_relation_child();

COMMENT ON TABLE relation_children IS 'Join table for hierarchical relation sequences';

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

-- ==============================================================================
-- TABLE: compression_hints
-- ==============================================================================
--
-- Stores compression/encoding metadata for efficient storage and retrieval.
-- Supports byte-pair encoding (BPE), run-length encoding (RLE), etc.
--
-- Examples:
--   - Frequent composition "the" appears 10,000 times → use BPE token
--   - Repeating pattern "aaaaa" → RLE: 5×'a'
--   - Common phrase "according to" → single token
--
CREATE TABLE compression_hints (
    id BIGSERIAL PRIMARY KEY,

    -- Target hash (composition or relation)
    target_hash BYTEA NOT NULL,
    target_type VARCHAR(20) NOT NULL CHECK (target_type IN ('composition', 'relation')),

    -- Compression algorithm
    algorithm VARCHAR(50) NOT NULL, -- 'BPE', 'RLE', 'DICTIONARY', etc.

    -- Algorithm-specific parameters (JSONB for flexibility)
    parameters JSONB NOT NULL DEFAULT '{}'::JSONB,

    -- Statistics
    frequency_count INTEGER NOT NULL DEFAULT 1,
    compression_ratio DOUBLE PRECISION, -- original_size / compressed_size

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX idx_compression_hints_target ON compression_hints (target_hash, target_type);
CREATE INDEX idx_compression_hints_algorithm ON compression_hints (algorithm);
CREATE INDEX idx_compression_hints_frequency ON compression_hints (frequency_count DESC);

COMMENT ON TABLE compression_hints IS 'Compression and encoding metadata for efficient storage';

-- ==============================================================================
-- VIEWS: Hierarchical queries
-- ==============================================================================

-- View: Full relation tree (recursive)
CREATE OR REPLACE VIEW v_relation_tree AS
WITH RECURSIVE relation_tree AS (
    -- Base case: level 1 relations (sequences of compositions)
    SELECT
        r.hash AS relation_hash,
        r.level,
        rc.position,
        rc.child_hash,
        rc.child_type,
        1 AS depth,
        ARRAY[r.hash] AS path
    FROM
        relations r
        JOIN relation_children rc ON r.hash = rc.relation_hash
    WHERE
        r.level = 1

    UNION ALL

    -- Recursive case: deeper levels
    SELECT
        r.hash AS relation_hash,
        r.level,
        rc.position,
        rc.child_hash,
        rc.child_type,
        rt.depth + 1 AS depth,
        rt.path || r.hash AS path
    FROM
        relations r
        JOIN relation_children rc ON r.hash = rc.relation_hash
        JOIN relation_tree rt ON r.hash = rt.child_hash
    WHERE
        rc.child_type = 'relation'
        AND NOT (r.hash = ANY(rt.path)) -- Prevent cycles
)
SELECT * FROM relation_tree;

COMMENT ON VIEW v_relation_tree IS 'Recursive view of the complete relation hierarchy';

-- View: Document summary (top-level relations with metadata)
CREATE OR REPLACE VIEW v_documents AS
SELECT
    r.hash,
    r.level,
    r.length AS num_children,
    r.geometric_length,
    r.metadata->>'type' AS doc_type,
    r.metadata->>'title' AS title,
    r.metadata->>'author' AS author,
    r.created_at,
    t.total_distance,
    t.fractal_dimension
FROM
    relations r
    LEFT JOIN trajectories t ON r.hash = t.relation_hash
WHERE
    r.metadata->>'type' IN ('document', 'book', 'article', 'chapter')
ORDER BY
    r.created_at DESC;

COMMENT ON VIEW v_documents IS 'Top-level documents with metadata';

-- ==============================================================================
-- FUNCTIONS: Hierarchical operations
-- ==============================================================================

-- Function: Compute trajectory tortuosity
CREATE OR REPLACE FUNCTION compute_tortuosity(relation_hash_param BYTEA)
RETURNS DOUBLE PRECISION
LANGUAGE plpgsql
AS $$
DECLARE
    total_dist DOUBLE PRECISION;
    start_point RECORD;
    end_point RECORD;
    straight_dist DOUBLE PRECISION;
BEGIN
    -- Get total trajectory distance
    SELECT geometric_length INTO total_dist
    FROM relations
    WHERE hash = relation_hash_param;

    -- Get start and end points
    SELECT centroid_x, centroid_y, centroid_z, centroid_w INTO start_point
    FROM relations
    WHERE hash = (
        SELECT child_hash
        FROM relation_children
        WHERE relation_hash = relation_hash_param
        ORDER BY position
        LIMIT 1
    );

    SELECT centroid_x, centroid_y, centroid_z, centroid_w INTO end_point
    FROM relations
    WHERE hash = (
        SELECT child_hash
        FROM relation_children
        WHERE relation_hash = relation_hash_param
        ORDER BY position DESC
        LIMIT 1
    );

    -- Compute straight-line distance
    straight_dist := SQRT(
        POWER(end_point.centroid_x - start_point.centroid_x, 2) +
        POWER(end_point.centroid_y - start_point.centroid_y, 2) +
        POWER(end_point.centroid_z - start_point.centroid_z, 2) +
        POWER(end_point.centroid_w - start_point.centroid_w, 2)
    );

    -- Avoid division by zero
    IF straight_dist < 1e-10 THEN
        RETURN 1.0;
    END IF;

    RETURN total_dist / straight_dist;
END;
$$;

COMMENT ON FUNCTION compute_tortuosity IS 'Compute trajectory tortuosity (total_dist / straight_dist)';

-- Function: Find similar documents by trajectory shape
CREATE OR REPLACE FUNCTION find_similar_trajectories(
    target_hash BYTEA,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    hash BYTEA,
    doc_title TEXT,
    similarity DOUBLE PRECISION
)
LANGUAGE SQL
AS $$
    SELECT
        r.hash,
        r.metadata->>'title' AS doc_title,
        1.0 / (1.0 + ABS(t1.tortuosity - t2.tortuosity) +
               ABS(COALESCE(t1.fractal_dimension, 0) - COALESCE(t2.fractal_dimension, 0))
        ) AS similarity
    FROM
        relations r
        JOIN trajectories t2 ON r.hash = t2.relation_hash
        CROSS JOIN (
            SELECT tortuosity, fractal_dimension
            FROM trajectories
            WHERE relation_hash = target_hash
        ) t1
    WHERE
        r.hash != target_hash
    ORDER BY
        similarity DESC
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_similar_trajectories IS 'Find documents with similar trajectory shapes';

-- ==============================================================================
-- EXAMPLE USAGE
-- ==============================================================================

/*
-- Example 1: Store "Call me Ishmael" as a level 1 relation (sentence)

-- First, ensure compositions exist for each word
-- (Assume "Call", "me", "Ishmael" are already in compositions table with hashes hash_call, hash_me, hash_ishmael)

-- Insert the relation
INSERT INTO relations (hash, level, length, centroid_x, centroid_y, centroid_z, centroid_w,
                       hilbert_index, bbox_min_x, bbox_min_y, bbox_min_z, bbox_min_w,
                       bbox_max_x, bbox_max_y, bbox_max_z, bbox_max_w,
                       geometric_length, parent_type, metadata)
VALUES (
    hash_sentence,  -- BLAKE3 hash of sequence
    1,              -- Level 1 (sequence of compositions)
    3,              -- Length (3 words)
    0.5, 0.5, 0.5, 0.5,  -- Centroid (example)
    12345,          -- Hilbert index
    0.3, 0.3, 0.3, 0.3,  -- Bounding box min
    0.7, 0.7, 0.7, 0.7,  -- Bounding box max
    0.123,          -- Geometric length
    'composition',  -- Parent type
    '{"type": "sentence", "source": "Moby Dick", "chapter": 1}'::JSONB
);

-- Insert children
INSERT INTO relation_children (relation_hash, child_hash, child_type, position) VALUES
    (hash_sentence, hash_call, 'composition', 0),
    (hash_sentence, hash_me, 'composition', 1),
    (hash_sentence, hash_ishmael, 'composition', 2);

-- Example 2: Store Chapter 1 as a level 2 relation (sequence of sentences)

INSERT INTO relations (hash, level, ..., parent_type, metadata)
VALUES (
    hash_chapter1,
    2,              -- Level 2 (sequence of relations)
    ...,
    'relation',     -- Parent type
    '{"type": "chapter", "number": 1, "book": "Moby Dick"}'::JSONB
);

INSERT INTO relation_children (relation_hash, child_hash, child_type, position) VALUES
    (hash_chapter1, hash_sentence1, 'relation', 0),
    (hash_chapter1, hash_sentence2, 'relation', 1),
    ...;
*/
