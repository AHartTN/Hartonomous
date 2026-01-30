-- View: Document summary (top-level relations with metadata)
CREATE OR REPLACE VIEW v_documents AS
SELECT
    r.hash,
    r.level,
    r.length AS num_children,
    r.geometric_length,
    r.metadata->>'type' AS doc_type,
    r.metadata->>'title' AS title,
    r.metadata->>'author' AS author,
    r.created_at,
    t.total_distance,
    t.fractal_dimension
FROM
    relations r
    LEFT JOIN trajectories t ON r.hash = t.relation_hash
WHERE
    r.metadata->>'type' IN ('document', 'book', 'article', 'chapter')
ORDER BY
    r.created_at DESC;

COMMENT ON VIEW v_documents IS 'Top-level documents with metadata';