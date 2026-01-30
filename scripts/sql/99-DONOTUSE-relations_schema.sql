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
