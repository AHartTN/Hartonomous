-- Unsigned 16-bit Integer Domain (0 to 2^16-1)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_type WHERE typname = 'uint16'
    ) THEN
        CREATE DOMAIN uint16 AS bytea
            CHECK (octet_length(VALUE) = 2);
        COMMENT ON DOMAIN uint16 IS 'Unsigned 16-bit integer domain (0 to 2^16-1) stored as 2-byte bytea';
    END IF;
END $$;