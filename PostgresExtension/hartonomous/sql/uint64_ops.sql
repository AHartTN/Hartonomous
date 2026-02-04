-- ==============================================================================
-- UINT64 Native Operators
-- ==============================================================================

CREATE OR REPLACE FUNCTION uint64_add(hartonomous.uint64, hartonomous.uint64)
RETURNS hartonomous.uint64
AS 'MODULE_PATHNAME', 'uint64_add'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION uint64_to_double(hartonomous.uint64)
RETURNS DOUBLE PRECISION
AS 'MODULE_PATHNAME', 'uint64_to_double'
LANGUAGE C IMMUTABLE STRICT;

CREATE OPERATOR + (
    LEFTARG = hartonomous.uint64,
    RIGHTARG = hartonomous.uint64,
    PROCEDURE = uint64_add
);

-- Weighted average using native types
CREATE OR REPLACE FUNCTION hartonomous.weighted_elo_update(
    old_elo DOUBLE PRECISION, 
    old_obs hartonomous.uint64, 
    new_elo DOUBLE PRECISION, 
    new_obs hartonomous.uint64
)
RETURNS DOUBLE PRECISION
LANGUAGE SQL IMMUTABLE AS $$
    SELECT (old_elo * uint64_to_double(old_obs) + new_elo * uint64_to_double(new_obs)) / 
           (uint64_to_double(old_obs) + uint64_to_double(new_obs));
$$;
