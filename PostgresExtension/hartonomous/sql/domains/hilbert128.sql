-- hilbert128.sql
CREATE DOMAIN hartonomous.HILBERT128 AS hartonomous.UINT128;
COMMENT ON DOMAIN hartonomous.HILBERT128 IS 'Alias for UINT128 representing a Hilbert curve index';