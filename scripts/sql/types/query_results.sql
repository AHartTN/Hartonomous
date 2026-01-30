-- Query result
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'query_result') THEN
        CREATE TYPE query_result AS (
            text TEXT,
            confidence DOUBLE PRECISION
        );
    END IF;
END $$;