-- Unsigned 128-bit Integer Domain (0 to 2^128-1)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_type WHERE typname = 'uint128'
    ) THEN
        CREATE DOMAIN uint128 AS bytea
            CHECK (octet_length(VALUE) = 16);
        ALTER DOMAIN uint128
            SET STORAGE PLAIN;
        COMMENT ON DOMAIN uint128 IS 'Unsigned 128-bit integer domain (0 to 2^128-1) stored as 16-byte bytea';
    END IF;
END $$;
