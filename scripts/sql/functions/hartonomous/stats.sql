-- ==============================================================================
-- Function: Get Database Stats
-- ==============================================================================

CREATE OR REPLACE FUNCTION hartonomous.stats()
RETURNS TABLE (
    table_name TEXT,
    row_count BIGINT,
    total_size TEXT
)
LANGUAGE plpgsql
STABLE
AS $$
BEGIN
    RETURN QUERY
    SELECT 'Atom'::TEXT, COUNT(*), pg_size_pretty(pg_total_relation_size('Atom'))::TEXT
    FROM Atom
    UNION ALL
    SELECT 'Composition'::TEXT, COUNT(*), pg_size_pretty(pg_total_relation_size('Composition'))::TEXT
    FROM Composition
    UNION ALL
    SELECT 'Relation'::TEXT, COUNT(*), pg_size_pretty(pg_total_relation_size('Relation'))::TEXT
    FROM Relation
    UNION ALL
    SELECT 'Physicality'::TEXT, COUNT(*), pg_size_pretty(pg_total_relation_size('Physicality'))::TEXT
    FROM Physicality;
END;
$$;

COMMENT ON FUNCTION hartonomous.stats IS 'Get row counts and sizes for core tables';