CREATE OR REPLACE FUNCTION hartonomous.schema_version()
RETURNS TABLE (
    version INTEGER,
    description TEXT,
    applied_at TIMESTAMP WITH TIME ZONE
)
LANGUAGE sql
STABLE
AS $$
    SELECT version, description, applied_at
    FROM hartonomous_internal.schema_version
    ORDER BY version;
$$;