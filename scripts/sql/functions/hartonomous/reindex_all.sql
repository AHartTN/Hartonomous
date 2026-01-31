CREATE OR REPLACE FUNCTION hartonomous.reindex_all()
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    REINDEX TABLE Physicality;
    REINDEX TABLE Atom;
    REINDEX TABLE Composition;
    REINDEX TABLE CompositionSequence;
    REINDEX TABLE Relation;
    REINDEX TABLE RelationSequence;

    RAISE NOTICE 'REINDEX complete for all Hartonomous tables';
END;
$$;