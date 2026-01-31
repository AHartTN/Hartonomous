-- setup_db.sql
-- Run this script as a PostgreSQL superuser (e.g., 'postgres')

-- Create a dedicated role for the UCD database
-- You can set a strong password here or let the user be prompted
CREATE ROLE ucd_user WITH
    LOGIN
    PASSWORD 'your_secure_password_here'; -- !!! CHANGE THIS PASSWORD !!!

-- Create the UCD database
CREATE DATABASE UCD
    WITH
    OWNER = ucd_user
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

-- Grant all privileges on the database to the UCD user
GRANT ALL PRIVILEGES ON DATABASE UCD TO ucd_user;

-- Connect to the newly created database and set search path for the user
\c UCD ucd_user;

-- Set search_path for the ucd_user role
ALTER ROLE ucd_user SET search_path TO public;

\echo 'Database UCD and role ucd_user created successfully.'
\echo 'Remember to update UCD_DB_USER, UCD_DB_PASSWORD, UCD_DB_NAME environment variables.'
