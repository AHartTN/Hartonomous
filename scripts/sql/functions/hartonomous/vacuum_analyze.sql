CREATE OR REPLACE FUNCTION hartonomous.vacuum_analyze()
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    VACUUM ANALYZE hartonomous.atoms;
    VACUUM ANALYZE hartonomous.compositions;
    VACUUM ANALYZE hartonomous.relations;
    VACUUM ANALYZE hartonomous.composition_atoms;
    VACUUM ANALYZE hartonomous.relation_children;
    VACUUM ANALYZE hartonomous.metadata;

    RAISE NOTICE 'VACUUM ANALYZE complete for all Hartonomous tables';
END;
$$;