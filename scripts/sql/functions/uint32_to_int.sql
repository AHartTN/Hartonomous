-- ==============================================================================
-- Helper Function: Convert UINT32 (bytea) to INTEGER
-- ==============================================================================

CREATE OR REPLACE FUNCTION uint32_to_int(val uint32)
RETURNS INTEGER
LANGUAGE SQL
IMMUTABLE
PARALLEL SAFE
AS $$
    SELECT ('x' || encode(val, 'hex'))::bit(32)::integer;
$$;

COMMENT ON FUNCTION uint32_to_int(uint32) IS 'Converts UINT32 domain (4-byte bytea) to INTEGER for use in standard PostgreSQL functions';
