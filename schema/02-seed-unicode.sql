-- ==============================================================================
-- Seed Unicode Codepoints
-- ==============================================================================
-- This file is IDEMPOTENT - safe to run multiple times
-- Seeds atoms table with common Unicode codepoints
--
-- For initial setup, we seed:
-- - ASCII printable (32-126): 95 codepoints
-- - Common Unicode ranges (will be expanded in future)
--
-- NOTE: Full Unicode support will require C++ engine for projection
-- This seeds a minimal set for testing

SET search_path TO hartonomous, public;

-- Create temporary function for seeding (will be replaced by C++ engine)
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

    -- Temporary: Simple hash function (will use BLAKE3 from engine)
    cp_hash TEXT;

    -- Temporary: Simple S³ projection (will use proper projection from engine)
    theta DOUBLE PRECISION;
    phi DOUBLE PRECISION;
    psi DOUBLE PRECISION;
BEGIN
    FOR cp IN start_cp..end_cp LOOP
        -- Temporary hash: MD5 of codepoint (will be replaced by BLAKE3)
        hash_bytes := digest(cp::TEXT, 'md5');

        -- Check if already exists
        IF NOT EXISTS (SELECT 1 FROM hartonomous.atoms WHERE codepoint = cp) THEN
            -- Temporary projection: Map codepoint to angles on S³
            -- This is a placeholder - proper projection from C++ engine will replace this
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

-- Seed ASCII printable characters (32-126)
DO $$
DECLARE
    inserted INTEGER;
BEGIN
    RAISE NOTICE 'Seeding ASCII printable characters (32-126)...';

    inserted := hartonomous_internal.seed_unicode_batch(32, 126);

    RAISE NOTICE 'Inserted % new ASCII codepoints', inserted;
END $$;

-- Seed common Latin-1 Supplement (128-255)
DO $$
DECLARE
    inserted INTEGER;
BEGIN
    RAISE NOTICE 'Seeding Latin-1 Supplement (128-255)...';

    inserted := hartonomous_internal.seed_unicode_batch(128, 255);

    RAISE NOTICE 'Inserted % new Latin-1 codepoints', inserted;
END $$;

-- Seed basic multilingual plane common characters
-- We'll do this in batches to avoid long transactions

-- Latin Extended-A (256-383)
DO $$
DECLARE
    inserted INTEGER;
BEGIN
    RAISE NOTICE 'Seeding Latin Extended-A (256-383)...';

    inserted := hartonomous_internal.seed_unicode_batch(256, 383);

    RAISE NOTICE 'Inserted % new Latin Extended-A codepoints', inserted;
END $$;

-- Special characters and symbols (common ones)
DO $$
DECLARE
    inserted INTEGER;
    common_symbols INTEGER[] := ARRAY[
        8217,  -- ' (right single quotation mark)
        8216,  -- ' (left single quotation mark)
        8220,  -- " (left double quotation mark)
        8221,  -- " (right double quotation mark)
        8211,  -- – (en dash)
        8212,  -- — (em dash)
        8230,  -- … (ellipsis)
        169,   -- © (copyright)
        174,   -- ® (registered)
        8482,  -- ™ (trademark)
        8364,  -- € (euro sign)
        163,   -- £ (pound sign)
        165    -- ¥ (yen sign)
    ];
    cp INTEGER;
    hash_bytes BYTEA;
    theta DOUBLE PRECISION;
    phi DOUBLE PRECISION;
    psi DOUBLE PRECISION;
BEGIN
    RAISE NOTICE 'Seeding common symbols...';
    inserted := 0;

    FOREACH cp IN ARRAY common_symbols LOOP
        hash_bytes := digest(cp::TEXT, 'md5');

        IF NOT EXISTS (SELECT 1 FROM hartonomous.atoms WHERE codepoint = cp) THEN
            theta := (cp::DOUBLE PRECISION / 1114111.0) * PI();
            phi := ((cp % 360)::DOUBLE PRECISION / 360.0) * 2 * PI();
            psi := ((cp % 180)::DOUBLE PRECISION / 180.0) * PI();

            INSERT INTO hartonomous.atoms (
                hash, codepoint,
                centroid_x, centroid_y, centroid_z, centroid_w,
                hilbert_index
            ) VALUES (
                hash_bytes, cp,
                COS(theta / 2) * COS((phi + psi) / 2),
                COS(theta / 2) * SIN((phi + psi) / 2),
                SIN(theta / 2) * COS((phi - psi) / 2),
                SIN(theta / 2) * SIN((phi - psi) / 2),
                cp
            )
            ON CONFLICT (codepoint) DO NOTHING;

            inserted := inserted + 1;
        END IF;
    END LOOP;

    RAISE NOTICE 'Inserted % new symbol codepoints', inserted;
END $$;

-- Create helper view for Unicode info
CREATE OR REPLACE VIEW hartonomous.unicode_atoms AS
SELECT
    codepoint,
    CHR(codepoint) AS character,
    hash,
    centroid_x, centroid_y, centroid_z, centroid_w,
    hilbert_index,
    created_at
FROM hartonomous.atoms
ORDER BY codepoint;

-- Statistics
DO $$
DECLARE
    total_atoms INTEGER;
    ascii_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO total_atoms FROM hartonomous.atoms;
    SELECT COUNT(*) INTO ascii_count FROM hartonomous.atoms WHERE codepoint BETWEEN 32 AND 126;

    RAISE NOTICE '';
    RAISE NOTICE 'Unicode seeding complete:';
    RAISE NOTICE '  Total atoms: %', total_atoms;
    RAISE NOTICE '  ASCII printable: %', ascii_count;
    RAISE NOTICE '';
    RAISE NOTICE 'NOTE: This uses placeholder projections.';
    RAISE NOTICE 'For production, use C++ engine with proper BLAKE3 and S³ projection.';
    RAISE NOTICE '';
END $$;

-- Record schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (3, 'Unicode atoms seeded (placeholder projections)')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;
