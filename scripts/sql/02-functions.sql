-- ==============================================================================
-- Hartonomous Helper Functions
-- ==============================================================================cd sc

SET search_path TO hartonomous, public;

\i functions/uint32_to_int.sql
\i functions/uint64_to_bigint.sql

\i functions/hartonomous/find_composition.sql
\i functions/hartonomous/find_related_compositions.sql
\i functions/hartonomous/reindex_all.sql
\i functions/hartonomous/repair_inconsistencies.sql
\i functions/hartonomous/schema_version.sql
\i functions/hartonomous/simple_hash.sql
\i functions/hartonomous/stats.sql
\i functions/hartonomous/upsert_composition.sql
\i functions/hartonomous/vacuum_analyze.sql
\i functions/hartonomous/version.sql

\i functions/geodesic_distance_s3.sql
\i functions/add_edge_vote.sql
\i functions/answer_question.sql
\i functions/check_rate_limit.sql
\i functions/check_s3_normalization.sql
\i functions/compute_tortuosity.sql
\i functions/find_nearest_atoms.sql
\i functions/find_similar_compositions.sql
\i functions/find_similar_trajectories.sql
\i functions/flag_content.sql
\i functions/fuzzy_search.sql
\i functions/multi_hop_reasoning.sql
\i functions/phoenetic_search.sql
\i functions/semantic_search.sql
\i functions/validate_relation_child.sql

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
