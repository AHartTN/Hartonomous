CREATE OPERATOR CLASS hartonomous.gist_s3_ops
FOR TYPE geometry USING gist AS
    OPERATOR 1 hartonomous.<=> (geometry, geometry) FOR ORDER BY pg_catalog.float_ops,
    FUNCTION 1 hartonomous.gist_s3_consistent (internal, geometry, int4),
    FUNCTION 2 hartonomous.gist_s3_union (internal, internal),
    FUNCTION 3 hartonomous.gist_s3_compress (internal),
    FUNCTION 4 hartonomous.gist_s3_decompress (internal),
    FUNCTION 5 hartonomous.gist_s3_penalty (internal, internal, internal),
    FUNCTION 6 hartonomous.gist_s3_picksplit (internal, internal),
    FUNCTION 7 hartonomous.gist_s3_same (internal, internal, internal),
    FUNCTION 8 hartonomous.gist_s3_distance (internal, geometry, int4);