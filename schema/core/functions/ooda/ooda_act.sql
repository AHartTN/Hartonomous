-- ============================================================================
-- OODA Act Phase
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Execute optimization with safety checks
-- ============================================================================

CREATE OR REPLACE FUNCTION ooda_act(p_hypothesis TEXT)
RETURNS TEXT
LANGUAGE plpgsql
AS $$
DECLARE
    v_result TEXT;
BEGIN
    -- Safety check: require manual approval for dangerous operations
    IF p_hypothesis LIKE '%DROP%' OR p_hypothesis LIKE '%TRUNCATE%' THEN
        v_result := 'REQUIRES_APPROVAL: ' || p_hypothesis;
        
        INSERT INTO ooda_audit_log (hypothesis, result)
        VALUES (p_hypothesis, v_result);
        
        RETURN v_result;
    END IF;
    
    -- Execute optimization
    BEGIN
        EXECUTE p_hypothesis;
        v_result := 'SUCCESS: ' || p_hypothesis;
    EXCEPTION WHEN OTHERS THEN
        v_result := 'FAILED: ' || SQLERRM;
    END;
    
    -- Log action
    INSERT INTO ooda_audit_log (hypothesis, result)
    VALUES (p_hypothesis, v_result);
    
    RETURN v_result;
END;
$$;

COMMENT ON FUNCTION ooda_act(TEXT) IS 
'ACT phase: execute optimization with safety checks.';
