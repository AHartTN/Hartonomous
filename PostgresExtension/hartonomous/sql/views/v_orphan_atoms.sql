CREATE OR REPLACE VIEW hartonomous.v_orphan_atoms AS
SELECT 
    a.Codepoint,
    am.Name,
    am.Block,
    am.Script
FROM hartonomous.Atom a
JOIN hartonomous.AtomMetadata am ON a.Id = am.AtomId
LEFT JOIN hartonomous.CompositionSequence cs ON a.Id = cs.AtomId
WHERE cs.Id IS NULL;
