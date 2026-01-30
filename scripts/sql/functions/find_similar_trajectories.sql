-- Function: Find similar documents by trajectory shape
CREATE OR REPLACE FUNCTION find_similar_trajectories(
    target_hash BYTEA,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    hash BYTEA,
    doc_title TEXT,
    similarity DOUBLE PRECISION
)
LANGUAGE SQL
AS $$
    SELECT
        r.hash,
        r.metadata->>'title' AS doc_title,
        1.0 / (1.0 + ABS(t1.tortuosity - t2.tortuosity) +
               ABS(COALESCE(t1.fractal_dimension, 0) - COALESCE(t2.fractal_dimension, 0))
        ) AS similarity
    FROM
        relations r
        JOIN trajectories t2 ON r.hash = t2.relation_hash
        CROSS JOIN (
            SELECT tortuosity, fractal_dimension
            FROM trajectories
            WHERE relation_hash = target_hash
        ) t1
    WHERE
        r.hash != target_hash
    ORDER BY
        similarity DESC
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_similar_trajectories IS 'Find documents with similar trajectory shapes';