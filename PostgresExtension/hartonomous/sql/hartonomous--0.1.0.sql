-- hartonomous--0.1.0.sql
-- Modular entry point for the Hartonomous Engine wrapper.

-- Wrapper Functions
\i 'functions/hashing.sql'
\i 'functions/projection.sql'
\i 'functions/analysis.sql'
\i 'functions/ingestion.sql'

-- Native unsigned integer operators
\i 'uint64_ops.sql'
\i 'uint128_ops.sql'

-- Versioning
CREATE OR REPLACE FUNCTION hartonomous_version()
RETURNS text AS 'MODULE_PATHNAME', 'hartonomous_version'
LANGUAGE C IMMUTABLE STRICT;
