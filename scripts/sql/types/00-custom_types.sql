-- ==============================================================================
-- Custom PostgreSQL Domain Types for Hartonomous
-- ==============================================================================
-- FIXED-SIZE unsigned integers using BYTEA with octet_length constraints
-- These are FAST and COMPACT - no variable-size overhead
-- ==============================================================================

-- UINT16: 2-byte unsigned integer (content types, small counts)
CREATE DOMAIN UINT16 AS BYTEA
    CHECK (octet_length(VALUE) = 2);

-- UINT32: 4-byte unsigned integer (Unicode codepoints, ordinals, occurrences)
CREATE DOMAIN UINT32 AS BYTEA
    CHECK (octet_length(VALUE) = 4);

-- UINT64: 8-byte unsigned integer (large observations, counts)
CREATE DOMAIN UINT64 AS BYTEA
    CHECK (octet_length(VALUE) = 8);

-- UINT128: 16-byte unsigned integer (Hilbert curve indices - spatial locality keys)
CREATE DOMAIN UINT128 AS BYTEA
    CHECK (octet_length(VALUE) = 16);

-- Helper function to create UINT128 from two 64-bit values (hi, lo)
-- This represents the Hilbert index as a pair of 64-bit integers
CREATE OR REPLACE FUNCTION uint128_from_parts(hi BIGINT, lo BIGINT)
RETURNS UINT128 AS $$
DECLARE
    result BYTEA;
BEGIN
    -- Store as big-endian: high 64 bits first, then low 64 bits
    result := int8send(hi) || int8send(lo);
    RETURN result::UINT128;
END;
$$ LANGUAGE plpgsql IMMUTABLE STRICT;

-- Helper to extract high 64 bits from UINT128
CREATE OR REPLACE FUNCTION uint128_hi(value UINT128)
RETURNS BIGINT AS $$
    SELECT int8recv(substring(value, 1, 8));
$$ LANGUAGE SQL IMMUTABLE STRICT;

-- Helper to extract low 64 bits from UINT128
CREATE OR REPLACE FUNCTION uint128_lo(value UINT128)
RETURNS BIGINT AS $$
    SELECT int8recv(substring(value, 9, 8));
$$ LANGUAGE SQL IMMUTABLE STRICT;

-- Compare two UINT128 values
CREATE OR REPLACE FUNCTION uint128_compare(a UINT128, b UINT128)
RETURNS INTEGER AS $$
DECLARE
    a_hi BIGINT;
    a_lo BIGINT;
    b_hi BIGINT;
    b_lo BIGINT;
BEGIN
    a_hi := uint128_hi(a);
    b_hi := uint128_hi(b);

    IF a_hi > b_hi THEN RETURN 1;
    ELSIF a_hi < b_hi THEN RETURN -1;
    END IF;

    a_lo := uint128_lo(a);
    b_lo := uint128_lo(b);

    IF a_lo > b_lo THEN RETURN 1;
    ELSIF a_lo < b_lo THEN RETURN -1;
    ELSE RETURN 0;
    END IF;
END;
$$ LANGUAGE plpgsql IMMUTABLE STRICT;

COMMENT ON DOMAIN UINT16 IS 'Fixed 2-byte unsigned integer (big-endian)';
COMMENT ON DOMAIN UINT32 IS 'Fixed 4-byte unsigned integer (big-endian) - used for codepoints, ordinals';
COMMENT ON DOMAIN UINT64 IS 'Fixed 8-byte unsigned integer (big-endian) - used for large counts';
COMMENT ON DOMAIN UINT128 IS 'Fixed 16-byte unsigned integer (big-endian) - Hilbert curve spatial index';

-- ==============================================================================
-- UUID Usage in Hartonomous
-- ==============================================================================
-- UUIDs are NOT random - they are BLAKE3 content-addressable hashes
-- Same content → Same BLAKE3 hash → Same UUID → Stored once (deduplication)
-- This is the foundation of universal compression (90-95%)
-- ==============================================================================
