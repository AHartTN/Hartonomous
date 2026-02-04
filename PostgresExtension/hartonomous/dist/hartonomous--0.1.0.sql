-- Generated from sql/hartonomous--0.1.0.sql
-- hartonomous--0.1.0.sql
-- Modular entry point for the Hartonomous Engine wrapper.

-- Wrapper Functions
-- Including functions/hashing.sql
-- Hashing
CREATE OR REPLACE FUNCTION blake3_hash(text)
RETURNS bytea AS 'MODULE_PATHNAME', 'blake3_hash'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION blake3_hash_codepoint(int)
RETURNS bytea AS 'MODULE_PATHNAME', 'blake3_hash_codepoint'
LANGUAGE C IMMUTABLE STRICT;

-- Including functions/projection.sql
-- Projection
CREATE OR REPLACE FUNCTION codepoint_to_s3(int)
RETURNS text AS 'MODULE_PATHNAME', 'codepoint_to_s3'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION codepoint_to_hilbert(int)
RETURNS bytea AS 'MODULE_PATHNAME', 'codepoint_to_hilbert'
LANGUAGE C IMMUTABLE STRICT;

-- Including functions/analysis.sql
-- Analysis
CREATE OR REPLACE FUNCTION compute_centroid(float8[][])
RETURNS text AS 'MODULE_PATHNAME', 'compute_centroid'
LANGUAGE C IMMUTABLE STRICT;

-- Including functions/ingestion.sql
-- Ingestion Shim
CREATE TYPE ingestion_stats_result AS (
    atoms_new bigint,
    compositions_new bigint,
    relations_new bigint,
    original_bytes bigint,
    stored_bytes bigint,
    compression_ratio float8
);

CREATE OR REPLACE FUNCTION ingest_text(text)
RETURNS ingestion_stats_result AS 'MODULE_PATHNAME', 'ingest_text'
LANGUAGE C VOLATILE STRICT;


-- Versioning
CREATE OR REPLACE FUNCTION hartonomous_version()
RETURNS text AS 'MODULE_PATHNAME', 'hartonomous_version'
LANGUAGE C IMMUTABLE STRICT;
