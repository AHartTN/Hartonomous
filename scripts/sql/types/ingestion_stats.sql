-- Ingestion statistics
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'ingestion_stats') THEN
        CREATE TYPE ingestion_stats AS (
            atoms_new BIGINT,
            atoms_existing BIGINT,
            compositions_new BIGINT,
            compositions_existing BIGINT,
            relations_total BIGINT,
            original_bytes BIGINT,
            stored_bytes BIGINT,
            compression_ratio DOUBLE PRECISION
        );
    END IF;
END $$;