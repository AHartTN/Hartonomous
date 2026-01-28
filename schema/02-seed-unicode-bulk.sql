-- ==============================================================================
-- Bulk Seed ALL Unicode Codepoints - SET-BASED, NOT RBAR
-- ==============================================================================
-- Seeds ALL 1,114,112 Unicode codepoints using bulk INSERT
-- Uses generate_series for set-based operation

SET search_path TO hartonomous, public;

\timing on

DO $$
DECLARE
    start_time TIMESTAMP := clock_timestamp();
BEGIN
    RAISE NOTICE 'Starting bulk Unicode seed...';
    RAISE NOTICE 'Generating 1,114,112 codepoints...';
END $$;

-- Bulk insert ALL Unicode codepoints in ONE operation
INSERT INTO hartonomous.atoms (hash, codepoint, centroid_x, centroid_y, centroid_z, centroid_w, hilbert_index)
SELECT
    digest(cp::TEXT, 'md5') AS hash,
    cp AS codepoint,
    COS(theta / 2) * COS((phi + psi) / 2) AS centroid_x,
    COS(theta / 2) * SIN((phi + psi) / 2) AS centroid_y,
    SIN(theta / 2) * COS((phi - psi) / 2) AS centroid_z,
    SIN(theta / 2) * SIN((phi - psi) / 2) AS centroid_w,
    cp AS hilbert_index
FROM (
    SELECT
        cp,
        (cp::DOUBLE PRECISION / 1114111.0) * PI() AS theta,
        ((cp % 360)::DOUBLE PRECISION / 360.0) * 2 * PI() AS phi,
        ((cp % 180)::DOUBLE PRECISION / 180.0) * PI() AS psi
    FROM generate_series(0, 1114111) AS cp
) AS projections
ON CONFLICT (codepoint) DO NOTHING;

-- Statistics
DO $$
DECLARE
    total INTEGER;
    elapsed INTERVAL := clock_timestamp() - (SELECT MIN(created_at) FROM hartonomous.atoms);
BEGIN
    SELECT COUNT(*) INTO total FROM hartonomous.atoms;

    RAISE NOTICE '';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Unicode Seed Complete!';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Total atoms: %', total;
    RAISE NOTICE 'Time elapsed: %', elapsed;
    RAISE NOTICE '';
END $$;

-- Create indexes if not exist
CREATE INDEX IF NOT EXISTS idx_atoms_codepoint ON hartonomous.atoms(codepoint);
CREATE INDEX IF NOT EXISTS idx_atoms_hilbert ON hartonomous.atoms(hilbert_index);

-- Analyze for query optimization
ANALYZE hartonomous.atoms;

-- Final report
SELECT
    'Total Atoms' AS metric,
    COUNT(*)::TEXT AS value
FROM hartonomous.atoms
UNION ALL
SELECT
    'ASCII (0-127)',
    COUNT(*)::TEXT
FROM hartonomous.atoms WHERE codepoint BETWEEN 0 AND 127
UNION ALL
SELECT
    'BMP (0-65535)',
    COUNT(*)::TEXT
FROM hartonomous.atoms WHERE codepoint BETWEEN 0 AND 65535
UNION ALL
SELECT
    'Supplementary (65536+)',
    COUNT(*)::TEXT
FROM hartonomous.atoms WHERE codepoint > 65535
UNION ALL
SELECT
    'Table Size',
    pg_size_pretty(pg_total_relation_size('hartonomous.atoms'))
FROM hartonomous.atoms LIMIT 1;

\timing off
