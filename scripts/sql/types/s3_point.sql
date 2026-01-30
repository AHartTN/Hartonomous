-- 4D point on S³ sphere
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 's3_point') THEN
        CREATE TYPE s3_point AS (
            x DOUBLE PRECISION,
            y DOUBLE PRECISION,
            z DOUBLE PRECISION,
            w DOUBLE PRECISION
        );
    END IF;
END $$;