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