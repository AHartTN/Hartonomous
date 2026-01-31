CREATE OR REPLACE FUNCTION hartonomous.vacuum_analyze()
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    VACUUM ANALYZE Physicality;
    VACUUM ANALYZE Atom;
    VACUUM ANALYZE Composition;
    VACUUM ANALYZE CompositionSequence;
    VACUUM ANALYZE Relation;
    VACUUM ANALYZE RelationSequence;

    RAISE NOTICE 'VACUUM ANALYZE complete for all Hartonomous tables';
END;
$$;