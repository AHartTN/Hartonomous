-- uint64.sql
CREATE DOMAIN hartonomous.UINT64 AS BYTEA
    CHECK (octet_length(VALUE) = 8);
COMMENT ON DOMAIN hartonomous.UINT64 IS 'Fixed 8-byte unsigned integer (big-endian) - used for large counts';