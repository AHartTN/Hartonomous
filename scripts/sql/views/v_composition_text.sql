-- ==============================================================================
-- View: Reconstruct text from Atoms for debugging/display
-- ==============================================================================

CREATE OR REPLACE VIEW v_composition_text AS
SELECT
    c.Id AS composition_id,
    STRING_AGG(
        REPEAT(
            -- Convert UINT32 (bytea) to Integer for chr()
            chr(('x' || encode(a.Codepoint, 'hex'))::bit(32)::int), 
            cs.Occurrences
        ),
        '' ORDER BY cs.Ordinal
    ) AS reconstructed_text
FROM
    Composition c
JOIN
    CompositionSequence cs ON c.Id = cs.CompositionId
JOIN
    Atom a ON cs.AtomId = a.Id
GROUP BY
    c.Id;

COMMENT ON VIEW v_composition_text IS 'Reconstructs composition text from atom sequence (on-demand)';