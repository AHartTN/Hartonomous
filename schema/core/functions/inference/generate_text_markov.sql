-- ============================================================================
-- Generate Text via Markov Chain
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Generate text sequences from atom relations (next-token prediction)
-- ============================================================================

CREATE OR REPLACE FUNCTION generate_text_markov(
    p_seed_atom_id BIGINT,
    p_length INTEGER DEFAULT 50,
    p_temperature REAL DEFAULT 1.0
)
RETURNS TEXT
LANGUAGE plpgsql
AS $$
DECLARE
    v_current_atom BIGINT;
    v_next_atom BIGINT;
    v_generated_text TEXT := '';
    v_current_text TEXT;
    v_random REAL;
    v_cumulative_prob REAL;
BEGIN
    v_current_atom := p_seed_atom_id;
    
    FOR i IN 1..p_length LOOP
        -- Get current atom text
        SELECT canonical_text INTO v_current_text
        FROM atom WHERE atom_id = v_current_atom;
        
        v_generated_text := v_generated_text || v_current_text;
        
        -- Sample next atom via temperature-scaled probabilities
        v_random := random();
        v_cumulative_prob := 0.0;
        
        SELECT atom_id INTO v_next_atom
        FROM (
            SELECT 
                ar.target_atom_id AS atom_id,
                -- Temperature scaling: higher temp = more randomness
                EXP(ar.weight / p_temperature) / 
                SUM(EXP(ar.weight / p_temperature)) OVER () AS prob,
                SUM(EXP(ar.weight / p_temperature) / 
                    SUM(EXP(ar.weight / p_temperature)) OVER ()) 
                    OVER (ORDER BY ar.weight DESC) AS cumulative_prob
            FROM atom_relation ar
            WHERE ar.source_atom_id = v_current_atom
            ORDER BY ar.weight DESC
            LIMIT 20  -- Consider top 20 transitions
        ) weighted
        WHERE cumulative_prob >= v_random
        LIMIT 1;
        
        EXIT WHEN v_next_atom IS NULL;  -- Dead end
        
        v_current_atom := v_next_atom;
    END LOOP;
    
    RETURN v_generated_text;
END;
$$;

COMMENT ON FUNCTION generate_text_markov(BIGINT, INTEGER, REAL) IS 
'Generate text via Markov chain over atom relations.
Temperature: 0.0 = greedy (deterministic), 1.0 = balanced, 2.0 = creative (random).
Uses relation weights as transition probabilities.';
