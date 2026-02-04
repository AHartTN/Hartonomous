-- Projection
CREATE OR REPLACE FUNCTION codepoint_to_s3(int)
RETURNS text AS 'MODULE_PATHNAME', 'codepoint_to_s3'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION codepoint_to_hilbert(int)
RETURNS bytea AS 'MODULE_PATHNAME', 'codepoint_to_hilbert'
LANGUAGE C IMMUTABLE STRICT;
