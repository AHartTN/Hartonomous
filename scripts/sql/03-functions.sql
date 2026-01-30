-- ==============================================================================
-- Hartonomous Helper Functions
-- ==============================================================================
-- This file is IDEMPOTENT - safe to run multiple times
-- Creates SQL helper functions for common operations

SET search_path TO hartonomous, public;

\i functions/hartonomous/version.sql
\i functions/hartonomous/schema_version.sql
\i functions/hartonomous/simple_hash.sql
\i functions/hartonomous/find_composition.sql
\i functions/hartonomous/find_related_compositions.sql
\i functions/hartonomous/stats.sql
\i functions/hartonomous/repair_inconsistencies.sql
\i functions/hartonomous/upsert_composition.sql
\i functions/hartonomous/vacuum_analyze.sql
\i functions/hartonomous/reindex_all.sql

\i functions/answer_question.sql
\i functions/semantic_search.sql
\i functions/fuzzy_search.sql
\i functions/multi_hop_reasoning.sql
\i functions/phonetic_search.sql

-- Record schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (4, 'Helper functions and utilities')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Helper functions installed successfully';
    RAISE NOTICE 'Available functions:';
    RAISE NOTICE '  - hartonomous.version()';
    RAISE NOTICE '  - hartonomous.schema_version()';
    RAISE NOTICE '  - hartonomous.find_composition(text)';
    RAISE NOTICE '  - hartonomous.find_related_compositions(text, limit)';
    RAISE NOTICE '  - hartonomous.stats()';
    RAISE NOTICE '  - hartonomous.repair_inconsistencies()';
    RAISE NOTICE '  - hartonomous.vacuum_analyze()';
    RAISE NOTICE '  - hartonomous.reindex_all()';
END $$;
