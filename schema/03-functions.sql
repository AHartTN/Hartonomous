-- ==============================================================================
-- Hartonomous Helper Functions
-- ==============================================================================
-- This file is IDEMPOTENT - safe to run multiple times
-- Creates SQL helper functions for common operations

SET search_path TO hartonomous, public;

-- ==============================================================================
-- VERSION FUNCTIONS
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.version()
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT '0.1.0'::TEXT;
$$;

CREATE OR REPLACE FUNCTION hartonomous.schema_version()
RETURNS TABLE (
    version INTEGER,
    description TEXT,
    applied_at TIMESTAMP WITH TIME ZONE
)
LANGUAGE sql
STABLE
AS $$
    SELECT version, description, applied_at
    FROM hartonomous_internal.schema_version
    ORDER BY version;
$$;

-- ==============================================================================
-- HASH FUNCTIONS (Placeholders - will use BLAKE3 from C++ engine)
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.simple_hash(input TEXT)
RETURNS BYTEA
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT digest(input, 'sha256');
$$;

COMMENT ON FUNCTION hartonomous.simple_hash IS
'Placeholder hash function. Will be replaced by BLAKE3 from C++ engine.';

-- ==============================================================================
-- COMPOSITION LOOKUP
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.find_composition(search_text TEXT)
RETURNS TABLE (
    hash BYTEA,
    text TEXT,
    centroid_x DOUBLE PRECISION,
    centroid_y DOUBLE PRECISION,
    centroid_z DOUBLE PRECISION,
    centroid_w DOUBLE PRECISION
)
LANGUAGE sql
STABLE
AS $$
    SELECT hash, text, centroid_x, centroid_y, centroid_z, centroid_w
    FROM hartonomous.compositions
    WHERE LOWER(text) = LOWER(search_text)
    LIMIT 1;
$$;

-- ==============================================================================
-- RELATION TRAVERSAL
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.find_related_compositions(
    query_text TEXT,
    limit_count INTEGER DEFAULT 10
)
RETURNS TABLE (
    text TEXT,
    co_occurrence_count BIGINT
)
LANGUAGE sql
STABLE
AS $$
    WITH query_composition AS (
        SELECT hash
        FROM hartonomous.compositions
        WHERE LOWER(text) = LOWER(query_text)
        LIMIT 1
    ),
    query_relations AS (
        SELECT DISTINCT rc.relation_hash
        FROM hartonomous.relation_children rc
        CROSS JOIN query_composition qc
        WHERE rc.child_hash = qc.hash
          AND rc.child_type = 'composition'
    ),
    cooccurring_compositions AS (
        SELECT
            c.text,
            COUNT(DISTINCT qr.relation_hash) AS co_occurrence_count
        FROM query_relations qr
        JOIN hartonomous.relation_children rc ON rc.relation_hash = qr.relation_hash
        JOIN hartonomous.compositions c ON c.hash = rc.child_hash
        CROSS JOIN query_composition qc
        WHERE rc.child_type = 'composition'
          AND c.hash != qc.hash
        GROUP BY c.text
        ORDER BY co_occurrence_count DESC
        LIMIT limit_count
    )
    SELECT * FROM cooccurring_compositions;
$$;

-- ==============================================================================
-- STATISTICS
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.stats()
RETURNS TABLE (
    table_name TEXT,
    row_count BIGINT,
    total_size TEXT
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        'atoms' AS table_name,
        COUNT(*) AS row_count,
        pg_size_pretty(pg_total_relation_size('hartonomous.atoms')) AS total_size
    FROM hartonomous.atoms
    UNION ALL
    SELECT
        'compositions',
        COUNT(*),
        pg_size_pretty(pg_total_relation_size('hartonomous.compositions'))
    FROM hartonomous.compositions
    UNION ALL
    SELECT
        'relations',
        COUNT(*),
        pg_size_pretty(pg_total_relation_size('hartonomous.relations'))
    FROM hartonomous.relations
    UNION ALL
    SELECT
        'composition_atoms',
        COUNT(*),
        pg_size_pretty(pg_total_relation_size('hartonomous.composition_atoms'))
    FROM hartonomous.composition_atoms
    UNION ALL
    SELECT
        'relation_children',
        COUNT(*),
        pg_size_pretty(pg_total_relation_size('hartonomous.relation_children'))
    FROM hartonomous.relation_children;
$$;

-- ==============================================================================
-- REPAIR FUNCTIONS
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.repair_inconsistencies()
RETURNS TABLE (
    issue TEXT,
    fixed_count INTEGER
)
LANGUAGE plpgsql
AS $$
DECLARE
    orphaned_comp_atoms INTEGER;
    orphaned_rel_children INTEGER;
BEGIN
    -- Fix orphaned composition_atoms (compositions that don't exist)
    WITH deleted AS (
        DELETE FROM hartonomous.composition_atoms ca
        WHERE NOT EXISTS (
            SELECT 1 FROM hartonomous.compositions c WHERE c.hash = ca.composition_hash
        )
        RETURNING *
    )
    SELECT COUNT(*)::INTEGER INTO orphaned_comp_atoms FROM deleted;

    IF orphaned_comp_atoms > 0 THEN
        RETURN QUERY SELECT 'Orphaned composition_atoms'::TEXT, orphaned_comp_atoms;
    END IF;

    -- Fix orphaned relation_children
    WITH deleted AS (
        DELETE FROM hartonomous.relation_children rc
        WHERE NOT EXISTS (
            SELECT 1 FROM hartonomous.relations r WHERE r.hash = rc.relation_hash
        )
        RETURNING *
    )
    SELECT COUNT(*)::INTEGER INTO orphaned_rel_children FROM deleted;

    IF orphaned_rel_children > 0 THEN
        RETURN QUERY SELECT 'Orphaned relation_children'::TEXT, orphaned_rel_children;
    END IF;

    -- If nothing to fix
    IF orphaned_comp_atoms = 0 AND orphaned_rel_children = 0 THEN
        RETURN QUERY SELECT 'No issues found'::TEXT, 0::INTEGER;
    END IF;
END;
$$;

-- ==============================================================================
-- BULK INSERT HELPERS
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.upsert_composition(
    p_hash BYTEA,
    p_text TEXT,
    p_centroid_x DOUBLE PRECISION,
    p_centroid_y DOUBLE PRECISION,
    p_centroid_z DOUBLE PRECISION,
    p_centroid_w DOUBLE PRECISION,
    p_hilbert_index BIGINT
)
RETURNS VOID
LANGUAGE sql
AS $$
    INSERT INTO hartonomous.compositions (
        hash, text,
        centroid_x, centroid_y, centroid_z, centroid_w,
        centroid,
        hilbert_index,
        length
    ) VALUES (
        p_hash, p_text,
        p_centroid_x, p_centroid_y, p_centroid_z, p_centroid_w,
        ST_SetSRID(ST_MakePoint(p_centroid_x, p_centroid_y, p_centroid_z, p_centroid_w), 0),
        p_hilbert_index,
        length(p_text)
    )
    ON CONFLICT (hash) DO UPDATE SET
        access_count = hartonomous.compositions.access_count + 1;
$$;

-- ==============================================================================
-- MAINTENANCE
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.vacuum_analyze()
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    VACUUM ANALYZE hartonomous.atoms;
    VACUUM ANALYZE hartonomous.compositions;
    VACUUM ANALYZE hartonomous.relations;
    VACUUM ANALYZE hartonomous.composition_atoms;
    VACUUM ANALYZE hartonomous.relation_children;
    VACUUM ANALYZE hartonomous.metadata;

    RAISE NOTICE 'VACUUM ANALYZE complete for all Hartonomous tables';
END;
$$;

CREATE OR REPLACE FUNCTION hartonomous.reindex_all()
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    REINDEX TABLE hartonomous.atoms;
    REINDEX TABLE hartonomous.compositions;
    REINDEX TABLE hartonomous.relations;
    REINDEX TABLE hartonomous.composition_atoms;
    REINDEX TABLE hartonomous.relation_children;
    REINDEX TABLE hartonomous.metadata;

    RAISE NOTICE 'REINDEX complete for all Hartonomous tables';
END;
$$;

-- Record schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (4, 'Helper functions and utilities')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Helper functions installed successfully';
    RAISE NOTICE 'Available functions:';
    RAISE NOTICE '  - hartonomous.version()';
    RAISE NOTICE '  - hartonomous.schema_version()';
    RAISE NOTICE '  - hartonomous.find_composition(text)';
    RAISE NOTICE '  - hartonomous.find_related_compositions(text, limit)';
    RAISE NOTICE '  - hartonomous.stats()';
    RAISE NOTICE '  - hartonomous.repair_inconsistencies()';
    RAISE NOTICE '  - hartonomous.vacuum_analyze()';
    RAISE NOTICE '  - hartonomous.reindex_all()';
END $$;
