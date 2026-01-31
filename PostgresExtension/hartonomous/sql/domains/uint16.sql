CREATE DOMAIN hartonomous.uint16 AS integer
    CHECK (VALUE >= 0 AND VALUE <= 65535);
