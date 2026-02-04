-- uint32.sql
CREATE DOMAIN hartonomous.UINT32 AS BYTEA
    CHECK (octet_length(VALUE) = 4);
COMMENT ON DOMAIN hartonomous.UINT32 IS 'Fixed 4-byte unsigned integer (big-endian) - used for codepoints, ordinals';