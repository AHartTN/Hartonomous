-- ==============================================================================
-- Hartonomous Core Tables
-- ==============================================================================
-- This file is IDEMPOTENT - safe to run multiple times
-- Creates atoms, compositions, and relations tables

SET search_path TO hartonomous, public;

\i tables/Atom.sql
\i tables/Composition.sql
\i tables/AtomComposition.sql
\i tables/Relation.sql
\i tables/RelationChildren.sql
\i tables/Metadata.sql

-- Record schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (2, 'Core tables: atoms, compositions, relations')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Core tables installed successfully';
    RAISE NOTICE '  - atoms: % rows', (SELECT COUNT(*) FROM atoms);
    RAISE NOTICE '  - compositions: % rows', (SELECT COUNT(*) FROM compositions);
    RAISE NOTICE '  - relations: % rows', (SELECT COUNT(*) FROM relations);
END $$;
