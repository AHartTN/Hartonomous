-- ============================================================================
-- Find Error Clusters (Poison Atom Detection)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Detect which atoms cause hallucinations/errors
-- The "Dreaming" phase: heavy graph analytics without locking operational DB
-- ============================================================================

CREATE OR REPLACE FUNCTION find_error_clusters()
RETURNS TABLE(
    poison_atom_id BIGINT,
    error_count INTEGER,
    affected_inferences BIGINT[]
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM cypher('provenance', $$
        MATCH (e:Error)-[:TRACED_TO]->(poison:Atom)
        MATCH (poison)<-[:USED_ATOM]-(inf:Inference)
        WITH poison, COUNT(DISTINCT e) AS errors, COLLECT(DISTINCT inf.inference_id) AS inferences
        WHERE errors > 2
        RETURN 
            poison.atom_id::bigint,
            errors::int,
            inferences::bigint[]
        ORDER BY errors DESC
        LIMIT 100
    $$) AS (poison_atom_id bigint, error_count int, affected_inferences bigint[]);
END;
$$;

COMMENT ON FUNCTION find_error_clusters() IS 
'Detect poison atoms that cause multiple errors (hallucinations).
Run as background job (OODA Orient phase) - heavy graph analytics without locking.
The "Dreaming" mechanism: process experiences to find error patterns.';
