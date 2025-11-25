-- ============================================================================
-- HARTONOMOUS QUICK START EXAMPLES
-- Demonstrates core atomization, spatial queries, and OODA loop
-- ============================================================================

-- ==================================================
-- EXAMPLE 1: Basic Text Atomization
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 1: Atomize text and view atoms'
\echo '=================================================='

-- Atomize a simple phrase
SELECT atomize_text('Hello Hartonomous');

-- View the atoms created
SELECT 
    atom_id,
    canonical_text,
    reference_count,
    metadata->>'modality' as modality,
    created_at
FROM atom
WHERE metadata->>'modality' = 'character'
ORDER BY atom_id DESC
LIMIT 20;

\echo ''

-- ==================================================
-- EXAMPLE 2: Hierarchical Composition
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 2: Create word from character atoms'
\echo '=================================================='

DO $$
DECLARE
    v_char_ids BIGINT[];
    v_word_atom_id BIGINT;
    v_char_id BIGINT;
    v_idx INT := 0;
BEGIN
    -- Atomize the word "CAT" at character level
    v_char_ids := atomize_text('CAT');
    
    -- Create a word-level atom
    v_word_atom_id := atomize_value(
        convert_to('CAT', 'UTF8'),
        'CAT',
        '{"modality": "word"}'::jsonb
    );
    
    -- Compose: word contains characters
    FOREACH v_char_id IN ARRAY v_char_ids LOOP
        PERFORM create_composition(
            v_word_atom_id,
            v_char_id,
            v_idx,
            '{"composition_type": "word_character"}'::jsonb
        );
        v_idx := v_idx + 1;
    END LOOP;
    
    RAISE NOTICE 'Word atom ID: %, Character atoms: %', v_word_atom_id, v_char_ids;
END $$;

-- View the composition
SELECT 
    p.canonical_text as parent,
    c.canonical_text as component,
    ac.sequence_index as position
FROM atom_composition ac
JOIN atom p ON p.atom_id = ac.parent_atom_id
JOIN atom c ON c.atom_id = ac.component_atom_id
WHERE p.canonical_text = 'CAT'
ORDER BY ac.sequence_index;

\echo ''

-- ==================================================
-- EXAMPLE 3: Spatial Positioning
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 3: Compute spatial positions'
\echo '=================================================='

-- Create a few concept atoms
DO $$
BEGIN
    PERFORM atomize_value(convert_to('cat', 'UTF8'), 'cat', '{"modality": "concept"}'::jsonb);
    PERFORM atomize_value(convert_to('dog', 'UTF8'), 'dog', '{"modality": "concept"}'::jsonb);
    PERFORM atomize_value(convert_to('feline', 'UTF8'), 'feline', '{"modality": "concept"}'::jsonb);
    PERFORM atomize_value(convert_to('canine', 'UTF8'), 'canine', '{"modality": "concept"}'::jsonb);
END $$;

-- Compute spatial positions
UPDATE atom 
SET spatial_key = compute_spatial_position(atom_id)
WHERE metadata->>'modality' = 'concept'
  AND spatial_key IS NULL;

-- View spatial positions
SELECT 
    canonical_text,
    ST_AsText(spatial_key) as position,
    reference_count
FROM atom
WHERE metadata->>'modality' = 'concept'
  AND spatial_key IS NOT NULL
ORDER BY canonical_text;

\echo ''

-- ==================================================
-- EXAMPLE 4: Semantic Relations (Hebbian Learning)
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 4: Create semantic relations'
\echo '=================================================='

DO $$
DECLARE
    v_cat_id BIGINT;
    v_feline_id BIGINT;
    v_dog_id BIGINT;
    v_canine_id BIGINT;
BEGIN
    -- Get atom IDs
    SELECT atom_id INTO v_cat_id FROM atom WHERE canonical_text = 'cat';
    SELECT atom_id INTO v_feline_id FROM atom WHERE canonical_text = 'feline';
    SELECT atom_id INTO v_dog_id FROM atom WHERE canonical_text = 'dog';
    SELECT atom_id INTO v_canine_id FROM atom WHERE canonical_text = 'canine';
    
    -- Create semantic relations
    PERFORM create_relation(v_cat_id, v_feline_id, 'is_a', 0.9);
    PERFORM create_relation(v_dog_id, v_canine_id, 'is_a', 0.9);
    PERFORM create_relation(v_cat_id, v_dog_id, 'similar_to', 0.6);
    
    RAISE NOTICE 'Semantic relations created';
END $$;

-- View relations
SELECT 
    s.canonical_text as source,
    t.canonical_text as target,
    r.canonical_text as relation_type,
    ar.weight,
    ar.confidence
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom t ON t.atom_id = ar.target_atom_id
JOIN atom r ON r.atom_id = ar.relation_type_id
WHERE s.canonical_text IN ('cat', 'dog');

\echo ''

-- ==================================================
-- EXAMPLE 5: Hebbian Learning (Reinforce Synapse)
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 5: Reinforce synaptic connection'
\echo '=================================================='

DO $$
DECLARE
    v_cat_id BIGINT;
    v_feline_id BIGINT;
BEGIN
    SELECT atom_id INTO v_cat_id FROM atom WHERE canonical_text = 'cat';
    SELECT atom_id INTO v_feline_id FROM atom WHERE canonical_text = 'feline';
    
    -- Simulate co-activation (Hebbian learning)
    FOR i IN 1..5 LOOP
        PERFORM reinforce_synapse(v_cat_id, v_feline_id, 'is_a', 0.05);
    END LOOP;
    
    RAISE NOTICE 'Synapse reinforced 5 times';
END $$;

-- View strengthened relation
SELECT 
    s.canonical_text as source,
    t.canonical_text as target,
    ar.weight as synaptic_strength,
    ar.importance
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE s.canonical_text = 'cat' 
  AND t.canonical_text = 'feline';

\echo ''

-- ==================================================
-- EXAMPLE 6: Spatial Queries (K-Nearest Neighbors)
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 6: Find semantic neighbors'
\echo '=================================================='

-- Find atoms near "cat" in semantic space
SELECT 
    a.canonical_text,
    ST_Distance(
        a.spatial_key, 
        (SELECT spatial_key FROM atom WHERE canonical_text = 'cat')
    ) as semantic_distance,
    a.reference_count
FROM atom a
WHERE a.spatial_key IS NOT NULL
  AND a.canonical_text != 'cat'
ORDER BY a.spatial_key <-> (SELECT spatial_key FROM atom WHERE canonical_text = 'cat')
LIMIT 5;

\echo ''

-- ==================================================
-- EXAMPLE 7: OODA Loop (Autonomous Optimization)
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 7: Run OODA cycle'
\echo '=================================================='

-- Observe current system state
SELECT * FROM ooda_observe();

-- Run full OODA cycle (if issues found)
-- SELECT * FROM run_ooda_cycle();

\echo ''

-- ==================================================
-- EXAMPLE 8: Temporal Queries (Time Travel)
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 8: Query historical atom versions'
\echo '=================================================='

-- View current atoms
SELECT 
    atom_id,
    canonical_text,
    reference_count,
    valid_from
FROM atom
WHERE canonical_text IN ('cat', 'dog')
ORDER BY canonical_text;

-- View historical versions (if any exist)
SELECT 
    atom_id,
    canonical_text,
    reference_count,
    valid_from,
    valid_to
FROM atom_history
WHERE canonical_text IN ('cat', 'dog')
ORDER BY canonical_text, valid_from DESC;

\echo ''

-- ==================================================
-- EXAMPLE 9: System Statistics
-- ==================================================

\echo '=================================================='
\echo 'EXAMPLE 9: View system statistics'
\echo '=================================================='

SELECT 
    'Total atoms' as metric,
    COUNT(*)::TEXT as value
FROM atom

UNION ALL

SELECT 
    'Atoms with spatial positions',
    COUNT(*)::TEXT
FROM atom
WHERE spatial_key IS NOT NULL

UNION ALL

SELECT 
    'Total compositions',
    COUNT(*)::TEXT
FROM atom_composition

UNION ALL

SELECT 
    'Total relations',
    COUNT(*)::TEXT
FROM atom_relation

UNION ALL

SELECT 
    'Unique modalities',
    COUNT(DISTINCT metadata->>'modality')::TEXT
FROM atom

UNION ALL

SELECT 
    'Spatial index size',
    pg_size_pretty(pg_relation_size('idx_atom_spatial'))
FROM pg_class
WHERE relname = 'idx_atom_spatial';

\echo ''
\echo '=================================================='
\echo 'Quick Start Examples Complete!'
\echo '=================================================='
\echo ''
\echo 'Next steps:'
\echo '  1. Read docs/01-VISION.md for core concepts'
\echo '  2. Read docs/08-INGESTION.md for atomization patterns'
\echo '  3. Read docs/10-API-REFERENCE.md for full function reference'
\echo '=================================================='
