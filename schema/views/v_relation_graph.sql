-- ============================================================================
-- Relation Graph View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Graph structure for visualization and traversal
-- ============================================================================

CREATE VIEW v_relation_graph AS
SELECT 
    ar.relation_id,
    s.atom_id AS source_id,
    s.canonical_text AS source_text,
    s.spatial_key AS source_position,
    t.atom_id AS target_id,
    t.canonical_text AS target_text,
    t.spatial_key AS target_position,
    rt.canonical_text AS relation_type,
    ar.weight AS synaptic_strength,
    ar.confidence,
    ar.importance,
    ar.spatial_expression AS path_geometry,
    ar.last_accessed,
    EXTRACT(EPOCH FROM (now() - ar.last_accessed)) / 86400.0 AS days_since_used
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom t ON t.atom_id = ar.target_atom_id
JOIN atom rt ON rt.atom_id = ar.relation_type_id
WHERE ar.valid_to = 'infinity'::timestamptz;

COMMENT ON VIEW v_relation_graph IS 
'Complete relation graph with geometry paths for visualization.';
