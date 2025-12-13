-- Schema Verification Script
-- Checks database health and completeness

\echo '\n=== Hartonomous Schema Verification ==='

-- Extensions
\echo '\n--- Extensions ---'
SELECT 
    e.extname AS extension,
    e.extversion AS version,
    CASE WHEN e.extname IS NOT NULL THEN '✓' ELSE '✗' END AS status
FROM pg_extension e
WHERE e.extname IN ('postgis', 'postgis_topology', 'btree_gist', 'pg_stat_statements')
ORDER BY e.extname;

-- Tables
\echo '\n--- Tables ---'
SELECT 
    t.tablename,
    pg_size_pretty(pg_total_relation_size(quote_ident(t.schemaname) || '.' || quote_ident(t.tablename))) AS size,
    (SELECT COUNT(*) FROM pg_indexes WHERE tablename = t.tablename) AS indexes,
    CASE WHEN t.tablename IS NOT NULL THEN '✓' ELSE '✗' END AS status
FROM pg_tables t
WHERE t.schemaname = 'public'
  AND t.tablename IN ('atom', 'atom_compositions', 'cortex_landmarks', 'cortex_state')
ORDER BY t.tablename;

-- Row counts
\echo '\n--- Row Counts ---'
SELECT 'atom' AS table_name, COUNT(*) AS rows FROM atom
UNION ALL
SELECT 'atom_compositions', COUNT(*) FROM atom_compositions
UNION ALL
SELECT 'cortex_landmarks', COUNT(*) FROM cortex_landmarks
UNION ALL
SELECT 'cortex_state', COUNT(*) FROM cortex_state;

-- Critical indexes
\echo '\n--- Critical Indexes ---'
SELECT 
    i.indexname,
    i.tablename,
    pg_size_pretty(pg_relation_size(i.indexname::regclass)) AS size,
    a.amname AS type,
    CASE WHEN i.indexname IS NOT NULL THEN '✓' ELSE '✗' END AS status
FROM pg_indexes i
JOIN pg_class c ON c.relname = i.indexname
JOIN pg_am a ON a.oid = c.relam
WHERE i.schemaname = 'public'
  AND i.indexname IN ('idx_atoms_geom_gist', 'idx_atoms_hilbert', 'idx_atoms_class', 'idx_atoms_modality')
ORDER BY i.indexname;

-- Constraints
\echo '\n--- Constraints ---'
SELECT 
    conname AS constraint_name,
    contype AS type,
    pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE connamespace = 'public'::regnamespace
  AND conrelid IN ('atom'::regclass, 'atom_compositions'::regclass)
ORDER BY conrelid, contype, conname;

-- Geometry validation
\echo '\n--- Geometry Validation ---'
SELECT 
    'atom' AS table_name,
    COUNT(*) AS total_rows,
    COUNT(CASE WHEN geom IS NOT NULL THEN 1 END) AS has_geometry,
    COUNT(CASE WHEN ST_IsValid(geom) THEN 1 END) AS valid_geometry,
    COUNT(CASE WHEN ST_GeometryType(geom) = 'ST_Point' AND ST_SRID(geom) = 4326 THEN 1 END) AS correct_type
FROM atom;

-- Cortex state
\echo '\n--- Cortex State ---'
SELECT 
    model_version,
    atoms_processed,
    recalibrations,
    ROUND(avg_stress::numeric, 4) AS avg_stress,
    last_cycle_at
FROM cortex_state;

-- Hierarchy distribution
\echo '\n--- Hierarchy Distribution (Z-levels) ---'
SELECT 
    ROUND(ST_Z(geom)::numeric) AS z_level,
    COUNT(*) AS count,
    ROUND(100.0 * COUNT(*) / NULLIF((SELECT COUNT(*) FROM atom), 0), 2) AS percentage
FROM atom
GROUP BY ROUND(ST_Z(geom)::numeric)
ORDER BY z_level;

\echo '\n=== Verification Complete ===\n'
