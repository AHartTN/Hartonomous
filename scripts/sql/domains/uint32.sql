-- Unsigned 32-bit Integer Domain (0 to 2^32-1)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_type WHERE typname = 'uint32'
    ) THEN
        CREATE DOMAIN uint32 AS bytea
            CHECK (octet_length(VALUE) = 4);
        COMMENT ON DOMAIN uint32 IS 'Unsigned 32-bit integer domain (0 to 2^32-1) stored as 4-byte bytea';
    END IF;
END $$;
