-- ============================================================================
-- OODA Full Cycle
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Execute complete OODA cycle: Observe ? Orient ? Decide ? Act
-- ============================================================================

CREATE OR REPLACE FUNCTION run_ooda_cycle()
RETURNS TABLE(issue TEXT, action_taken TEXT, result TEXT)
LANGUAGE plpgsql
AS $$
DECLARE
    obs RECORD;
    orient RECORD;
    hypothesis TEXT;
    action_result TEXT;
BEGIN
    FOR obs IN SELECT * FROM ooda_observe() LOOP
        FOR orient IN SELECT * FROM ooda_orient(obs.issue, obs.metric, obs.atom_id) LOOP
            hypothesis := ooda_decide(orient.recommendation);
            
            IF hypothesis IS NOT NULL THEN
                action_result := ooda_act(hypothesis);
                
                -- Store provenance
                INSERT INTO ooda_provenance (observation, orientation, decision, action_result)
                VALUES (
                    jsonb_build_object('issue', obs.issue, 'metric', obs.metric, 'atom_id', obs.atom_id),
                    jsonb_build_object('root_cause', orient.root_cause, 'recommendation', orient.recommendation),
                    hypothesis,
                    action_result
                );
                
                RETURN QUERY SELECT obs.issue, hypothesis, action_result;
            END IF;
        END LOOP;
    END LOOP;
END;
$$;

COMMENT ON FUNCTION run_ooda_cycle() IS 
'Execute complete OODA cycle: Observe ? Orient ? Decide ? Act.';
