-- Generated from /home/ahart/Projects/Hartonomous/PostgresExtension/hartonomous/sql/hartonomous--0.1.0.sql
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

-- UINT64 native arithmetic (operates directly on raw bytes, full unsigned range)
CREATE OR REPLACE FUNCTION uint64_add(uint64, uint64)
RETURNS uint64 AS 'MODULE_PATHNAME', 'uint64_add'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION uint64_to_double(uint64)
RETURNS float8 AS 'MODULE_PATHNAME', 'uint64_to_double'
LANGUAGE C IMMUTABLE STRICT;

CREATE OPERATOR + (
    LEFTARG = uint64,
    RIGHTARG = uint64,
    PROCEDURE = uint64_add
);

-- Weighted ELO update using native uint64 observation counts
CREATE OR REPLACE FUNCTION weighted_elo_update(
    old_elo DOUBLE PRECISION,
    old_obs uint64,
    new_elo DOUBLE PRECISION,
    new_obs uint64
)
RETURNS DOUBLE PRECISION
LANGUAGE SQL IMMUTABLE AS $$
    SELECT (old_elo * uint64_to_double(old_obs) + new_elo * uint64_to_double(new_obs)) /
           (uint64_to_double(old_obs) + uint64_to_double(new_obs));
$$;

-- UINT128 operations
CREATE OR REPLACE FUNCTION uint128_from_parts(bigint, bigint)
RETURNS uint128 AS 'MODULE_PATHNAME', 'uint128_from_parts'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION uint128_hi(uint128)
RETURNS bigint AS 'MODULE_PATHNAME', 'uint128_hi'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION uint128_lo(uint128)
RETURNS bigint AS 'MODULE_PATHNAME', 'uint128_lo'
LANGUAGE C IMMUTABLE STRICT;
