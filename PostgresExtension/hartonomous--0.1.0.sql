-- complain if script is sourced in psql, rather than via CREATE EXTENSION
\echo Use "CREATE EXTENSION hartonomous" to load this file. \quit

-- ============================================================================
-- Version Info
-- ============================================================================

CREATE FUNCTION hartonomous_version()
RETURNS text
AS 'MODULE_PATHNAME', 'hartonomous_version'
LANGUAGE C IMMUTABLE STRICT;

-- ============================================================================
-- BLAKE3 Hashing
-- ============================================================================

CREATE FUNCTION blake3_hash(text)
RETURNS bytea
AS 'MODULE_PATHNAME', 'blake3_hash'
LANGUAGE C IMMUTABLE STRICT;

COMMENT ON FUNCTION blake3_hash(text) IS 'Compute BLAKE3 hash of text';

CREATE FUNCTION blake3_hash_codepoint(integer)
RETURNS bytea
AS 'MODULE_PATHNAME', 'blake3_hash_codepoint'
LANGUAGE C IMMUTABLE STRICT;

COMMENT ON FUNCTION blake3_hash_codepoint(integer) IS 'Compute BLAKE3 hash of Unicode codepoint';

-- ============================================================================
-- Codepoint Projection
-- ============================================================================

CREATE TYPE s3_point AS (
    x double precision,
    y double precision,
    z double precision,
    w double precision
);

CREATE FUNCTION codepoint_to_s3(integer)
RETURNS s3_point
AS 'MODULE_PATHNAME', 'codepoint_to_s3'
LANGUAGE C IMMUTABLE STRICT;

COMMENT ON FUNCTION codepoint_to_s3(integer) IS 'Project Unicode codepoint to 4D unit sphere (S³)';

CREATE FUNCTION codepoint_to_hilbert(integer)
RETURNS bigint
AS 'MODULE_PATHNAME', 'codepoint_to_hilbert'
LANGUAGE C IMMUTABLE STRICT;

COMMENT ON FUNCTION codepoint_to_hilbert(integer) IS 'Map Unicode codepoint to Hilbert curve index';

-- ============================================================================
-- Centroid Computation
-- ============================================================================

CREATE FUNCTION compute_centroid(bytea[])
RETURNS s3_point
AS 'MODULE_PATHNAME', 'compute_centroid'
LANGUAGE C IMMUTABLE STRICT;

COMMENT ON FUNCTION compute_centroid(bytea[]) IS 'Compute centroid of multiple S³ points (average on sphere)';

-- ============================================================================
-- Text Ingestion
-- ============================================================================

CREATE TYPE ingest_stats AS (
    atoms_created integer,
    compositions_created integer,
    relations_created integer,
    total_time_ms integer
);

CREATE FUNCTION ingest_text(text)
RETURNS ingest_stats
AS 'MODULE_PATHNAME', 'ingest_text'
LANGUAGE C VOLATILE STRICT;

COMMENT ON FUNCTION ingest_text(text) IS 'Ingest text into Hartonomous knowledge graph';

-- ============================================================================
-- Semantic Query
-- ============================================================================

CREATE TYPE semantic_result AS (
    text text,
    confidence double precision,
    provenance text[]
);

CREATE FUNCTION semantic_search(text, integer DEFAULT 10)
RETURNS SETOF semantic_result
AS 'MODULE_PATHNAME', 'semantic_search'
LANGUAGE C VOLATILE STRICT;

COMMENT ON FUNCTION semantic_search(text, integer) IS 'Semantic search through knowledge graph';
