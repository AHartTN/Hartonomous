-- Unsigned Long Integer Domain (0 to 2^64-1)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_type WHERE typname = 'uint64'
    ) THEN
        CREATE DOMAIN uint64 AS bytea
            CHECK (octet_length(VALUE) = 8);
        ALTER DOMAIN uint64
            SET STORAGE PLAIN;
        COMMENT ON DOMAIN uint64 IS 'Unsigned 64-bit integer domain (0 to 2^64-1) stored as 8-byte bytea';
    END IF;
END $$;