-- ==============================================================================
-- VIEWS: Convenience views for common queries
-- ==============================================================================

-- View: Compositions with full atom details
CREATE VIEW v_composition_details AS
SELECT
    c.hash AS composition_hash,
    c.text,
    c.length,
    c.centroid_x, c.centroid_y, c.centroid_z, c.centroid_w,
    c.hilbert_index,
    c.geometric_length,
    ac.position,
    a.codepoint,
    a.s3_x, a.s3_y, a.s3_z, a.s3_w,
    a.category
FROM
    compositions c
    JOIN atom_compositions ac ON c.hash = ac.composition_hash
    JOIN atoms a ON ac.atom_hash = a.hash
ORDER BY
    c.hash, ac.position;

COMMENT ON VIEW v_composition_details IS 'Compositions with full atom sequence details';