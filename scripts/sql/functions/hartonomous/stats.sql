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