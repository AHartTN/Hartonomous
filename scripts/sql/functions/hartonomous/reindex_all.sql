CREATE OR REPLACE FUNCTION hartonomous.reindex_all()
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    REINDEX TABLE hartonomous.atoms;
    REINDEX TABLE hartonomous.compositions;
    REINDEX TABLE hartonomous.relations;
    REINDEX TABLE hartonomous.composition_atoms;
    REINDEX TABLE hartonomous.relation_children;
    REINDEX TABLE hartonomous.metadata;

    RAISE NOTICE 'REINDEX complete for all Hartonomous tables';
END;
$$;