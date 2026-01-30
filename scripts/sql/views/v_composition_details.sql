-- ==============================================================================
-- VIEWS: Convenience views for common queries
-- ==============================================================================

-- View: Compositions with full atom details
CREATE OR REPLACE VIEW v_composition_details AS
SELECT
    c.Id AS composition_id,
    -- Reconstruct text for display
    STRING_AGG(
        REPEAT(chr(a.Codepoint), cs.Occurrences),
        '' ORDER BY cs.Ordinal
    ) AS text,
    -- Geometry
    ST_X(p.Centroid) AS centroid_x,
    ST_Y(p.Centroid) AS centroid_y,
    ST_Z(p.Centroid) AS centroid_z,
    ST_M(p.Centroid) AS centroid_w,
    p.Hilbert AS hilbert_index,
    -- Sequence details
    cs.Ordinal AS position,
    cs.Occurrences,
    a.Codepoint,
    ST_X(ap.Centroid) AS atom_x,
    ST_Y(ap.Centroid) AS atom_y,
    ST_Z(ap.Centroid) AS atom_z,
    ST_M(ap.Centroid) AS atom_w
FROM
    Composition c
    JOIN Physicality p ON c.PhysicalityId = p.Id
    JOIN CompositionSequence cs ON c.Id = cs.CompositionId
    JOIN Atom a ON cs.AtomId = a.Id
    JOIN Physicality ap ON a.PhysicalityId = ap.Id
GROUP BY
    c.Id, p.Id, cs.Ordinal, cs.Occurrences, a.Id, ap.Id
ORDER BY
    c.Id, cs.Ordinal;

COMMENT ON VIEW v_composition_details IS 'Compositions with full atom sequence details (Text reconstructed)';