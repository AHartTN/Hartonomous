-- s3--0.1.0.sql
-- Core function/operator/opclass registration for s3 extension

\i 'functions/geodesic_distance_s3.sql'
\i 'functions/geodesic_distance_s3_fast.sql'
\i 'functions/euclidean_distance_4d.sql'
\i 'functions/normalize_pointzm_s3.sql'

\i 'functions/gist/gist_s3_support.sql'

\i 'operators/operator_geodesic_distance_s3.sql'
\i 'operators/operator_geodesic_distance_s3_fast.sql'
\i 'operators/operator_euclidean_distance_4d.sql'

\i 'opclass/gist_s3_ops.sql'