-- ============================================================================
-- Sparse Hilbert Regions View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Visualize compressed Hilbert regions (RLE)
-- ============================================================================

CREATE VIEW v_hilbert_regions AS
SELECT 
    a.atom_id,
    a.canonical_text,
    (a.metadata->>'hilbert_start')::BIGINT AS hilbert_start,
    (a.metadata->>'hilbert_end')::BIGINT AS hilbert_end,
    (a.metadata->>'region_size')::BIGINT AS pixels_represented,
    (a.metadata->>'r')::INTEGER AS r,
    (a.metadata->>'g')::INTEGER AS g,
    (a.metadata->>'b')::INTEGER AS b,
    a.spatial_key AS color_position,
    a.metadata->>'compression_type' AS compression_type,
    a.reference_count,
    a.created_at,
    a.metadata
FROM atom a
WHERE a.metadata->>'modality' = 'hilbert_region';

COMMENT ON VIEW v_hilbert_regions IS 
'RLE-compressed Hilbert regions: one atom represents many consecutive pixels.';
