-- cleanup_db.sql
-- Run this script as a PostgreSQL superuser (e.g., 'postgres')
-- ONLY for development/testing purposes!

-- Terminate all active connections to the UCD database
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = 'UCD';

-- Drop the UCD database
DROP DATABASE IF EXISTS UCD;

-- Drop the UCD user role
DROP ROLE IF EXISTS ucd_user;

\echo 'Database UCD and role ucd_user dropped successfully.'
