-- ============================================================================
-- Trace Inference Reasoning Chain
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: "Show me HOW you reached this conclusion"
-- ============================================================================

CREATE OR REPLACE FUNCTION trace_inference_reasoning(p_inference_id BIGINT)
RETURNS TABLE(
    step INTEGER,
    model_name TEXT,
    atom_used_id BIGINT,
    atom_used_text TEXT,
    result_atom_id BIGINT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM cypher('provenance', $$
        MATCH (inf:Inference {inference_id: $inference_id})-[:USED_MODEL]->(m:Model)
        MATCH (inf)-[:USED_ATOM]->(input:Atom)
        MATCH (inf)-[:RESULTED_IN]->(output:Atom)
        RETURN 
            1 AS step,
            m.model_name::text,
            input.atom_id::bigint,
            input.canonical_text::text,
            output.atom_id::bigint
    $$,
    agtype_build_map('inference_id', p_inference_id::agtype))
    AS (step int, model_name text, atom_used_id bigint, atom_used_text text, result_atom_id bigint);
END;
$$;

COMMENT ON FUNCTION trace_inference_reasoning(BIGINT) IS 
'Trace complete reasoning chain: Model + Input Atoms ? Output Atom.
Explainable AI: "This idea came from Atom A (Physics) + Atom B (Poetry) via GPT-4."';
