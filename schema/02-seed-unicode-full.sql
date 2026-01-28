-- ==============================================================================
-- Seed ALL Unicode Codepoints
-- ==============================================================================
-- This seeds ALL 1,114,112 possible Unicode codepoints (0x000000 to 0x10FFFF)
-- This is IDEMPOTENT - safe to run multiple times
--
-- Uses batch processing to avoid long transactions

SET search_path TO hartonomous, public;

-- Create seeding function with batch processing
CREATE OR REPLACE FUNCTION hartonomous_internal.seed_unicode_batch(
    start_cp INTEGER,
    end_cp INTEGER
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    cp INTEGER;
    hash_bytes BYTEA;
    inserted_count INTEGER := 0;
    theta DOUBLE PRECISION;
    phi DOUBLE PRECISION;
    psi DOUBLE PRECISION;
BEGIN
    FOR cp IN start_cp..end_cp LOOP
        -- Hash using MD5 (temporary - will be replaced by BLAKE3 from C++ engine)
        hash_bytes := digest(cp::TEXT, 'md5');

        -- Check if already exists
        IF NOT EXISTS (SELECT 1 FROM hartonomous.atoms WHERE codepoint = cp) THEN
            -- Simple S³ projection (temporary - will use proper projection from C++ engine)
            theta := (cp::DOUBLE PRECISION / 1114111.0) * PI();
            phi := ((cp % 360)::DOUBLE PRECISION / 360.0) * 2 * PI();
            psi := ((cp % 180)::DOUBLE PRECISION / 180.0) * PI();

            INSERT INTO hartonomous.atoms (
                hash,
                codepoint,
                centroid_x,
                centroid_y,
                centroid_z,
                centroid_w,
                hilbert_index
            ) VALUES (
                hash_bytes,
                cp,
                COS(theta / 2) * COS((phi + psi) / 2),
                COS(theta / 2) * SIN((phi + psi) / 2),
                SIN(theta / 2) * COS((phi - psi) / 2),
                SIN(theta / 2) * SIN((phi - psi) / 2),
                cp  -- Placeholder Hilbert index
            )
            ON CONFLICT (codepoint) DO NOTHING;

            inserted_count := inserted_count + 1;
        END IF;
    END LOOP;

    RETURN inserted_count;
END;
$$;

-- Seed ALL Unicode codepoints in batches of 10,000
-- Total: 1,114,112 codepoints (0x000000 to 0x10FFFF)
DO $$
DECLARE
    batch_size INTEGER := 10000;
    current_start INTEGER := 0;
    current_end INTEGER;
    total_codepoints INTEGER := 1114112;
    inserted INTEGER;
    total_inserted INTEGER := 0;
    batch_num INTEGER := 0;
    total_batches INTEGER;
    start_time TIMESTAMP;
    elapsed INTERVAL;
BEGIN
    start_time := clock_timestamp();
    total_batches := CEIL(total_codepoints::NUMERIC / batch_size);

    RAISE NOTICE '';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Seeding ALL Unicode Codepoints';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Total codepoints: %', total_codepoints;
    RAISE NOTICE 'Batch size: %', batch_size;
    RAISE NOTICE 'Total batches: %', total_batches;
    RAISE NOTICE '';

    WHILE current_start < total_codepoints LOOP
        current_end := LEAST(current_start + batch_size - 1, total_codepoints - 1);
        batch_num := batch_num + 1;

        inserted := hartonomous_internal.seed_unicode_batch(current_start, current_end);
        total_inserted := total_inserted + inserted;

        -- Progress report every 10 batches
        IF batch_num % 10 = 0 THEN
            elapsed := clock_timestamp() - start_time;
            RAISE NOTICE 'Progress: Batch %/% (%.1f%%) - % codepoints inserted - Elapsed: %',
                batch_num,
                total_batches,
                (batch_num::NUMERIC / total_batches * 100),
                total_inserted,
                elapsed;
        END IF;

        current_start := current_end + 1;
    END LOOP;

    elapsed := clock_timestamp() - start_time;

    RAISE NOTICE '';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Unicode Seeding Complete!';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Total codepoints inserted: %', total_inserted;
    RAISE NOTICE 'Total codepoints in database: %', (SELECT COUNT(*) FROM hartonomous.atoms);
    RAISE NOTICE 'Total time: %', elapsed;
    RAISE NOTICE '';
    RAISE NOTICE 'NOTE: This uses placeholder MD5 hashes and simple projections.';
    RAISE NOTICE 'For production, use C++ engine with BLAKE3 and proper S³ projection.';
    RAISE NOTICE '';
END $$;

-- Create helper view for Unicode info
CREATE OR REPLACE VIEW hartonomous.unicode_atoms AS
SELECT
    codepoint,
    CASE
        WHEN codepoint <= 1114111 THEN CHR(codepoint)
        ELSE NULL
    END AS character,
    hash,
    centroid_x, centroid_y, centroid_z, centroid_w,
    hilbert_index,
    created_at
FROM hartonomous.atoms
ORDER BY codepoint;

-- Record schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (5, 'ALL Unicode codepoints seeded (0x000000 to 0x10FFFF)')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Final statistics
DO $$
DECLARE
    total_atoms INTEGER;
    ascii_count INTEGER;
    bmp_count INTEGER;
    smp_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO total_atoms FROM hartonomous.atoms;
    SELECT COUNT(*) INTO ascii_count FROM hartonomous.atoms WHERE codepoint BETWEEN 0 AND 127;
    SELECT COUNT(*) INTO bmp_count FROM hartonomous.atoms WHERE codepoint BETWEEN 0 AND 65535;
    SELECT COUNT(*) INTO smp_count FROM hartonomous.atoms WHERE codepoint BETWEEN 65536 AND 1114111;

    RAISE NOTICE '';
    RAISE NOTICE 'Final Statistics:';
    RAISE NOTICE '  Total atoms: %', total_atoms;
    RAISE NOTICE '  ASCII (0-127): %', ascii_count;
    RAISE NOTICE '  BMP (0-65535): %', bmp_count;
    RAISE NOTICE '  SMP+ (65536-1114111): %', smp_count;
    RAISE NOTICE '';
END $$;
