-- ==============================================================================
-- Hartonomous Core Tables
-- ==============================================================================

SET search_path TO hartonomous, public;

\i tables/01-Tenant.sql
\i tables/02-Tenant-User.sql
\i tables/03-Content.sql
\i tables/04-Physicality.sql
\i tables/05-Atom.sql
\i tables/06-Composition.sql
\i tables/07-CompositionSequence.sql
\i tables/08-Relation.sql
\i tables/09-RelationSequence.sql
\i tables/10-RelationRating.sql
\i tables/11-RelationEvidence.sql
\i tables/12-ModelProjection.sql

\i views/v_composition_text.sql
\i views/v_composition_details.sql

-- Record schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (2, 'Core tables: atoms, compositions, relations')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Core tables installed successfully';
    RAISE NOTICE '  - tenant: % rows', (SELECT COUNT(*) FROM Tenant);
    RAISE NOTICE '  - tenantuser: % rows', (SELECT COUNT(*) FROM TenantUser);
    RAISE NOTICE '  - content: % rows', (SELECT COUNT(*) FROM Content);
    RAISE NOTICE '  - physicality: % rows', (SELECT COUNT(*) FROM Physicality);
    RAISE NOTICE '  - atoms: % rows', (SELECT COUNT(*) FROM Atom);
    RAISE NOTICE '  - compositions: % rows', (SELECT COUNT(*) FROM Composition);
    RAISE NOTICE '  - relations: % rows', (SELECT COUNT(*) FROM Relation);
    RAISE NOTICE '  - relation_rating: % rows', (SELECT COUNT(*) FROM RelationRating);
    RAISE NOTICE '  - relation_evidence: % rows', (SELECT COUNT(*) FROM RelationEvidence);
    RAISE NOTICE '  - model_projection: % rows', (SELECT COUNT(*) FROM ModelProjection);
END $$;
