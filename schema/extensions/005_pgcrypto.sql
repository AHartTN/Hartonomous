-- ============================================================================
-- pgcrypto Extension (SHA-256 hashing)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Required for content-addressable storage via digest() function
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

COMMENT ON EXTENSION pgcrypto IS 
'Cryptographic functions including SHA-256 digest for content addressing';
