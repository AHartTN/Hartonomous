-- ==============================================================================
-- Helper Function: Convert UINT64 (bytea) to BIGINT
-- ==============================================================================

CREATE OR REPLACE FUNCTION uint64_to_bigint(val uint64)
RETURNS BIGINT
LANGUAGE SQL
IMMUTABLE
PARALLEL SAFE
AS $$
    SELECT ('x' || encode(val, 'hex'))::bit(64)::bigint;
$$;

COMMENT ON FUNCTION uint64_to_bigint(uint64) IS 'Converts UINT64 domain (8-byte bytea) to BIGINT for use in standard PostgreSQL functions';
