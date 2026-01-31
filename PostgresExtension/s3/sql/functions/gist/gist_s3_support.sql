CREATE OR REPLACE FUNCTION hartonomous.gist_s3_consistent(internal, geometry, int4)
RETURNS bool
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_consistent';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_union(internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_union';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_compress(internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_compress';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_decompress(internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_decompress';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_penalty(internal, internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_penalty';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_picksplit(internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_picksplit';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_same(internal, internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_same';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_distance(internal, geometry, int4)
RETURNS float8
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_distance';
