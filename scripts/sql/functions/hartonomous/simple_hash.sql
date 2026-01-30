CREATE OR REPLACE FUNCTION hartonomous.simple_hash(input TEXT)
RETURNS BYTEA
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT digest(input, 'sha256');
$$;

COMMENT ON FUNCTION hartonomous.simple_hash IS
'Placeholder hash function. Will be replaced by BLAKE3 from C++ engine.';