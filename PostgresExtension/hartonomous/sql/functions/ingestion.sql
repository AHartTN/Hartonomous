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
