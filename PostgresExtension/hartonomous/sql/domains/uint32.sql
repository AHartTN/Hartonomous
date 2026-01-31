CREATE DOMAIN hartonomous.uint32 AS integer
    CHECK (VALUE >= 0 AND VALUE <= 4294967295);
