-- ============================================================================
-- Atom Metadata GIN Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_atom_metadata 
    ON atom USING GIN (metadata jsonb_path_ops);

COMMENT ON INDEX idx_atom_metadata IS 
'GIN index for efficient JSONB queries (modality, model_name, tenant_id, etc.)';
