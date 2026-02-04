-- uint128_ops.sql
-- ==============================================================================
-- UINT128 Native Operators
-- ==============================================================================

CREATE OR REPLACE FUNCTION uint128_from_parts(hi BIGINT, lo BIGINT)
RETURNS hartonomous.UINT128
AS 'MODULE_PATHNAME', 'uint128_from_parts'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION uint128_hi(hartonomous.UINT128)
RETURNS BIGINT
AS 'MODULE_PATHNAME', 'uint128_hi'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION uint128_lo(hartonomous.UINT128)
RETURNS BIGINT
AS 'MODULE_PATHNAME', 'uint128_lo'
LANGUAGE C IMMUTABLE STRICT;
