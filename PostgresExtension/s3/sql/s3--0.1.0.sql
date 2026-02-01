-- s3--0.1.0.sql
-- Core function/operator/opclass registration for s3 extension

-- Distance functions
\i 'functions/geodesic_distance_s3.sql'
\i 'functions/geodesic_distance_s3_fast.sql'
\i 'functions/euclidean_distance_4d.sql'

-- S³ geometry utilities
\i 'functions/normalize_pointzm_s3.sql'
\i 'functions/s3_is_valid.sql'
\i 'functions/s3_dwithin.sql'
\i 'functions/s3_interpolate.sql'
\i 'functions/s3_centroid.sql'

-- GIST index support
\i 'functions/gist/gist_s3_support.sql'

-- Operators
\i 'operators/operator_geodesic_distance_s3.sql'
\i 'operators/operator_geodesic_distance_s3_fast.sql'
\i 'operators/operator_euclidean_distance_4d.sql'

-- Operator classes for indexing
\i 'opclass/gist_s3_ops.sql'