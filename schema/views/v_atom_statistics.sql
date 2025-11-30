-- ============================================================================
-- Atom Statistics View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Aggregate statistics by modality
-- ============================================================================

CREATE VIEW v_atom_statistics AS
SELECT 
    metadata->>'modality' AS modality,
    COUNT(*) AS atom_count,
    SUM(reference_count) AS total_references,
    AVG(reference_count) AS avg_references,
    MAX(reference_count) AS max_references,
    COUNT(*) FILTER (WHERE spatial_key IS NOT NULL) AS positioned_count,
    COUNT(*) FILTER (WHERE spatial_key IS NULL) AS unpositioned_count,
    AVG(OCTET_LENGTH(atom_value)) AS avg_size_bytes,
    MIN(created_at) AS first_created,
    MAX(created_at) AS last_created
FROM atom
GROUP BY metadata->>'modality'
ORDER BY atom_count DESC;

COMMENT ON VIEW v_atom_statistics IS 
'Aggregate statistics grouped by modality for system monitoring.';
