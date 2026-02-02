CREATE OR REPLACE VIEW hartonomous.v_promoted_units AS
SELECT 
    c.Id,
    p.Hilbert,
    am.GeneralCategory,
    am.Block,
    am.Script
FROM hartonomous.Composition c
JOIN hartonomous.Physicality p ON c.PhysicalityId = p.Id
LEFT JOIN hartonomous.CompositionSequence cs ON c.Id = cs.CompositionId AND cs.Ordinal = 0
LEFT JOIN hartonomous.Atom a ON cs.AtomId = a.Id
LEFT JOIN hartonomous.AtomMetadata am ON a.Id = am.AtomId
ORDER BY c.CreatedAt DESC;