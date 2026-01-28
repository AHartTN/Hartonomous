-- ==============================================================================
-- Hartonomous Database Schema
-- ==============================================================================
--
-- This schema implements a content-addressable geometric database for storing
-- text data as 4D spatial structures.
--
-- Core Concepts:
--   - Atoms: Individual Unicode codepoints as 4D points on S³ (indivisible)
--   - Compositions: Sequences of atoms (n-grams) as 4D linestrings
--   - AtomComposition: Join table maintaining sequence order
--
-- Key Properties:
--   - Content-addressable: SAME CONTENT = SAME HASH = SAME RECORD
--   - Spatial indexing: Hilbert curve indices for fast nearest-neighbor queries
--   - Geometric queries: Find similar compositions via 4D distance
--   - Deduplication: Automatic via hash-based primary keys
--
-- ==============================================================================

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "btree_gist";

-- ==============================================================================
-- TABLE: atoms
-- ==============================================================================
--
-- Stores individual Unicode codepoints with their geometric representations.
-- Each codepoint appears ONCE, positioned semantically on the 3-sphere (S³).
--
-- Columns:
--   - hash: BLAKE3 hash of codepoint + context (PRIMARY KEY)
--   - codepoint: Unicode codepoint value (U+0000 to U+10FFFF)
--   - s3_x, s3_y, s3_z, s3_w: Position on 3-sphere (unit quaternion)
--   - s2_x, s2_y, s2_z: Hopf projection to 2-sphere (for visualization)
--   - hypercube_x/y/z/w: Coordinates in unit 4D hypercube [0,1]⁴
--   - hilbert_index: Hilbert space-filling curve index (spatial key)
--   - category: Semantic category (Latin, Digit, Emoji, etc.)
--   - created_at: Timestamp of first insertion
--
CREATE TABLE atoms (
    -- Primary key: Content hash (BLAKE3, 256 bits)
    hash BYTEA PRIMARY KEY CHECK (octet_length(hash) = 32),

    -- Unicode codepoint
    codepoint INTEGER NOT NULL CHECK (codepoint >= 0 AND codepoint <= 0x10FFFF),

    -- Position on S³ (3-sphere in 4D, unit quaternion)
    -- Constraint: s3_x² + s3_y² + s3_z² + s3_w² = 1
    s3_x DOUBLE PRECISION NOT NULL,
    s3_y DOUBLE PRECISION NOT NULL,
    s3_z DOUBLE PRECISION NOT NULL,
    s3_w DOUBLE PRECISION NOT NULL,

    -- Hopf fibration projection to S² (for 3D visualization)
    s2_x DOUBLE PRECISION NOT NULL,
    s2_y DOUBLE PRECISION NOT NULL,
    s2_z DOUBLE PRECISION NOT NULL,

    -- Hypercube coordinates [0, 1]⁴ (for Hilbert encoding)
    hypercube_x DOUBLE PRECISION NOT NULL CHECK (hypercube_x >= 0 AND hypercube_x <= 1),
    hypercube_y DOUBLE PRECISION NOT NULL CHECK (hypercube_y >= 0 AND hypercube_y <= 1),
    hypercube_z DOUBLE PRECISION NOT NULL CHECK (hypercube_z >= 0 AND hypercube_z <= 1),
    hypercube_w DOUBLE PRECISION NOT NULL CHECK (hypercube_w >= 0 AND hypercube_w <= 1),

    -- Hilbert curve index (64-bit spatial key)
    hilbert_index BIGINT NOT NULL,

    -- Semantic category
    category VARCHAR(50) NOT NULL,

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,

    -- Constraints
    CONSTRAINT atoms_s3_normalized CHECK (
        ABS((s3_x * s3_x + s3_y * s3_y + s3_z * s3_z + s3_w * s3_w) - 1.0) < 0.0001
    )
);

-- Indexes for fast spatial queries
CREATE INDEX idx_atoms_hilbert ON atoms USING BTREE (hilbert_index);
CREATE INDEX idx_atoms_codepoint ON atoms (codepoint);
CREATE INDEX idx_atoms_category ON atoms (category);

-- GiST index for multi-dimensional range queries (hypercube coords)
CREATE INDEX idx_atoms_hypercube ON atoms USING GIST (
    hypercube_x, hypercube_y, hypercube_z, hypercube_w
);

-- Comment
COMMENT ON TABLE atoms IS 'Individual Unicode codepoints with 4D geometric positions on S³';
COMMENT ON COLUMN atoms.hash IS 'BLAKE3 hash of codepoint + context (content-addressable key)';
COMMENT ON COLUMN atoms.hilbert_index IS 'Hilbert space-filling curve index for spatial queries';

-- ==============================================================================
-- TABLE: compositions
-- ==============================================================================
--
-- Stores sequences of atoms (n-grams) as 4D linestrings.
-- Examples: "the", "king", "hello world", etc.
--
-- Columns:
--   - hash: BLAKE3 hash of atom sequence (PRIMARY KEY)
--   - length: Number of atoms in the sequence
--   - centroid_x/y/z/w: Geometric centroid of the linestring in 4D
--   - hilbert_index: Hilbert index of the centroid (spatial key)
--   - text: Original text (for human readability, not for matching)
--   - created_at: Timestamp of first insertion
--
-- Key Property: SAME CONTENT = SAME HASH
--   "king" in "the king" = "king" in "king of the hill" (same hash)
--
CREATE TABLE compositions (
    -- Primary key: Content hash (BLAKE3 of atom sequence)
    hash BYTEA PRIMARY KEY CHECK (octet_length(hash) = 32),

    -- Sequence metadata
    length INTEGER NOT NULL CHECK (length > 0),

    -- Geometric centroid in 4D (average of atom positions)
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

    -- Linestring length (geodesic distance on S³)
    geometric_length DOUBLE PRECISION NOT NULL CHECK (geometric_length >= 0),

    -- Text representation (for debugging, not for matching)
    text TEXT NOT NULL,

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL
);

-- Indexes for fast spatial queries
CREATE INDEX idx_compositions_hilbert ON compositions USING BTREE (hilbert_index);
CREATE INDEX idx_compositions_length ON compositions (length);
CREATE INDEX idx_compositions_text ON compositions USING HASH (text); -- Fast exact lookup

-- GiST index for bounding box queries
CREATE INDEX idx_compositions_bbox ON compositions USING GIST (
    bbox_min_x, bbox_min_y, bbox_min_z, bbox_min_w,
    bbox_max_x, bbox_max_y, bbox_max_z, bbox_max_w
);

-- Full-text search index (optional, for traditional text queries)
CREATE INDEX idx_compositions_text_fts ON compositions USING GIN (to_tsvector('english', text));

-- Comment
COMMENT ON TABLE compositions IS 'Sequences of atoms (n-grams) as 4D linestrings with centroids';
COMMENT ON COLUMN compositions.hash IS 'BLAKE3 hash of atom sequence (content-addressable)';
COMMENT ON COLUMN compositions.centroid_x IS 'Geometric centroid X coordinate in 4D hypercube';
COMMENT ON COLUMN compositions.geometric_length IS 'Total geodesic distance along linestring on S³';

-- ==============================================================================
-- TABLE: atom_compositions
-- ==============================================================================
--
-- Join table maintaining the sequence order of atoms within compositions.
-- This is the ONLY place where sequence is stored (referential integrity).
--
-- Columns:
--   - id: Surrogate key (for Postgres efficiency)
--   - composition_hash: Foreign key to compositions table
--   - atom_hash: Foreign key to atoms table
--   - position: Position in sequence (0-indexed)
--
-- Example:
--   Composition "king" = [k, i, n, g]
--     (hash_king, hash_k, 0)
--     (hash_king, hash_i, 1)
--     (hash_king, hash_n, 2)
--     (hash_king, hash_g, 3)
--
CREATE TABLE atom_compositions (
    -- Surrogate key for efficient indexing
    id BIGSERIAL PRIMARY KEY,

    -- Foreign keys
    composition_hash BYTEA NOT NULL REFERENCES compositions(hash) ON DELETE CASCADE,
    atom_hash BYTEA NOT NULL REFERENCES atoms(hash) ON DELETE RESTRICT,

    -- Position in sequence (0-indexed)
    position INTEGER NOT NULL CHECK (position >= 0),

    -- Unique constraint: each position in a composition is unique
    CONSTRAINT atom_compositions_unique_position UNIQUE (composition_hash, position)
);

-- Indexes for fast queries
CREATE INDEX idx_atom_compositions_composition ON atom_compositions (composition_hash);
CREATE INDEX idx_atom_compositions_atom ON atom_compositions (atom_hash);

-- Index for efficient sequence reconstruction
CREATE INDEX idx_atom_compositions_sequence ON atom_compositions (composition_hash, position);

-- Comment
COMMENT ON TABLE atom_compositions IS 'Join table maintaining sequence order of atoms within compositions';
COMMENT ON COLUMN atom_compositions.position IS 'Position in sequence (0-indexed)';

-- ==============================================================================
-- VIEWS: Convenience views for common queries
-- ==============================================================================

-- View: Compositions with full atom details
CREATE VIEW v_composition_details AS
SELECT
    c.hash AS composition_hash,
    c.text,
    c.length,
    c.centroid_x, c.centroid_y, c.centroid_z, c.centroid_w,
    c.hilbert_index,
    c.geometric_length,
    ac.position,
    a.codepoint,
    a.s3_x, a.s3_y, a.s3_z, a.s3_w,
    a.category
FROM
    compositions c
    JOIN atom_compositions ac ON c.hash = ac.composition_hash
    JOIN atoms a ON ac.atom_hash = a.hash
ORDER BY
    c.hash, ac.position;

COMMENT ON VIEW v_composition_details IS 'Compositions with full atom sequence details';

-- ==============================================================================
-- FUNCTIONS: Geometric operations
-- ==============================================================================

-- Function: Calculate geodesic distance on S³ between two atoms
CREATE OR REPLACE FUNCTION geodesic_distance_s3(
    x1 DOUBLE PRECISION, y1 DOUBLE PRECISION, z1 DOUBLE PRECISION, w1 DOUBLE PRECISION,
    x2 DOUBLE PRECISION, y2 DOUBLE PRECISION, z2 DOUBLE PRECISION, w2 DOUBLE PRECISION
)
RETURNS DOUBLE PRECISION
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    SELECT ACOS(LEAST(1.0, GREATEST(-1.0, x1*x2 + y1*y2 + z1*z2 + w1*w2)));
$$;

COMMENT ON FUNCTION geodesic_distance_s3 IS 'Calculate geodesic distance (angle) between two points on S³';

-- Function: Find nearest atoms to a given 4D point
CREATE OR REPLACE FUNCTION find_nearest_atoms(
    target_x DOUBLE PRECISION,
    target_y DOUBLE PRECISION,
    target_z DOUBLE PRECISION,
    target_w DOUBLE PRECISION,
    max_results INTEGER DEFAULT 10
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
        geodesic_distance_s3(target_x, target_y, target_z, target_w,
                             a.s3_x, a.s3_y, a.s3_z, a.s3_w) AS distance
    FROM
        atoms a
    ORDER BY
        distance
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_nearest_atoms IS 'Find k-nearest atoms to a target 4D point on S³';

-- Function: Find similar compositions by centroid proximity
CREATE OR REPLACE FUNCTION find_similar_compositions(
    target_hash BYTEA,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    hash BYTEA,
    text TEXT,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        c2.hash,
        c2.text,
        geodesic_distance_s3(
            c1.centroid_x, c1.centroid_y, c1.centroid_z, c1.centroid_w,
            c2.centroid_x, c2.centroid_y, c2.centroid_z, c2.centroid_w
        ) AS distance
    FROM
        compositions c1,
        compositions c2
    WHERE
        c1.hash = target_hash
        AND c2.hash != target_hash
    ORDER BY
        distance
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_similar_compositions IS 'Find compositions with similar centroids to a target';

-- ==============================================================================
-- TRIGGERS: Maintain data integrity
-- ==============================================================================

-- Trigger: Validate S³ normalization on insert/update
CREATE OR REPLACE FUNCTION check_s3_normalization()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    norm_sq DOUBLE PRECISION;
BEGIN
    norm_sq := NEW.s3_x * NEW.s3_x + NEW.s3_y * NEW.s3_y +
               NEW.s3_z * NEW.s3_z + NEW.s3_w * NEW.s3_w;

    IF ABS(norm_sq - 1.0) > 0.0001 THEN
        RAISE EXCEPTION 'S³ point not normalized: ||p||² = %', norm_sq;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_atoms_s3_normalization
BEFORE INSERT OR UPDATE ON atoms
FOR EACH ROW
EXECUTE FUNCTION check_s3_normalization();

-- ==============================================================================
-- SAMPLE DATA (for testing)
-- ==============================================================================

-- Insert sample atoms (letters 'a', 'b', 'c')
-- Note: In production, these would be generated by the C++ engine

-- Example: Atom for 'a' (codepoint 97)
-- INSERT INTO atoms (hash, codepoint, s3_x, s3_y, s3_z, s3_w, ...)
-- VALUES (...);

-- ==============================================================================
-- GRANTS (adjust for your security model)
-- ==============================================================================

-- GRANT SELECT, INSERT ON atoms TO app_user;
-- GRANT SELECT, INSERT ON compositions TO app_user;
-- GRANT SELECT, INSERT ON atom_compositions TO app_user;
