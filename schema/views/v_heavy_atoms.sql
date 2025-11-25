-- ============================================================================
-- Heavy Atoms View (Landmarks)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- High reference count atoms that anchor semantic space
-- ============================================================================

CREATE VIEW v_heavy_atoms AS
SELECT 
    a.atom_id,
    a.canonical_text,
    a.spatial_key,
    a.reference_count AS atomic_mass,
    a.metadata->>'modality' AS modality,
    COUNT(DISTINCT ac.component_atom_id) AS composition_count,
    COUNT(DISTINCT ar.target_atom_id) AS relation_count,
    a.created_at,
    a.metadata
FROM atom a
LEFT JOIN atom_composition ac ON ac.parent_atom_id = a.atom_id
LEFT JOIN atom_relation ar ON ar.source_atom_id = a.atom_id
WHERE a.reference_count > 1000
GROUP BY a.atom_id
ORDER BY a.reference_count DESC;

COMMENT ON VIEW v_heavy_atoms IS 
'Landmark atoms with high reference counts that anchor semantic space.';
