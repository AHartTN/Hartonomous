-- uint16.sql
CREATE DOMAIN hartonomous.UINT16 AS BYTEA
    CHECK (octet_length(VALUE) = 2);
COMMENT ON DOMAIN hartonomous.UINT16 IS 'Fixed 2-byte unsigned integer (big-endian)';