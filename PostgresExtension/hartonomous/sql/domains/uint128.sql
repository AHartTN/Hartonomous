-- uint128.sql
CREATE DOMAIN hartonomous.UINT128 AS BYTEA
    CHECK (octet_length(VALUE) = 16);
COMMENT ON DOMAIN hartonomous.UINT128 IS 'Fixed 16-byte unsigned integer (big-endian) - Hilbert curve spatial index';