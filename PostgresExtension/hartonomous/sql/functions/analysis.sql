-- Analysis
CREATE OR REPLACE FUNCTION compute_centroid(float8[][])
RETURNS text AS 'MODULE_PATHNAME', 'compute_centroid'
LANGUAGE C IMMUTABLE STRICT;
