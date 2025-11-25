-- ============================================================================
-- Train Step (Backpropagation via Gradient Descent)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Update relation weights based on prediction error
-- ============================================================================

CREATE OR REPLACE FUNCTION train_step(
    p_input_atom_ids BIGINT[],
    p_target_atom_id BIGINT,
    p_learning_rate REAL DEFAULT 0.01
)
RETURNS REAL  -- Returns loss
LANGUAGE plpgsql
AS $$
DECLARE
    v_predicted_atom BIGINT;
    v_prediction_weight REAL;
    v_loss REAL;
    v_target_position GEOMETRY;
    v_predicted_position GEOMETRY;
BEGIN
    -- Get target position
    SELECT spatial_key INTO v_target_position
    FROM atom WHERE atom_id = p_target_atom_id;
    
    -- Predict next atom via attention (highest weight)
    SELECT atom_id, attention_weight
    INTO v_predicted_atom, v_prediction_weight
    FROM compute_attention(
        p_input_atom_ids[ARRAY_LENGTH(p_input_atom_ids, 1)],  -- Last input as query
        p_input_atom_ids[1:ARRAY_LENGTH(p_input_atom_ids, 1)-1]  -- Context
    )
    LIMIT 1;
    
    IF v_predicted_atom IS NULL THEN
        RETURN 999.0;  -- No prediction possible
    END IF;
    
    -- Compute loss (spatial distance = error)
    SELECT spatial_key INTO v_predicted_position
    FROM atom WHERE atom_id = v_predicted_atom;
    
    v_loss := ST_Distance(v_predicted_position, v_target_position);
    
    -- Gradient descent: update weights on input ? target relations
    FOR i IN 1..ARRAY_LENGTH(p_input_atom_ids, 1) LOOP
        -- Reinforce correct paths (Hebbian learning)
        IF v_predicted_atom = p_target_atom_id THEN
            PERFORM reinforce_synapse(
                p_input_atom_ids[i],
                p_target_atom_id,
                'next_token',
                p_learning_rate
            );
        ELSE
            -- Weaken incorrect paths
            PERFORM weaken_synapse(
                p_input_atom_ids[i],
                v_predicted_atom,
                'next_token',
                p_learning_rate * 0.5  -- Smaller penalty
            );
        END IF;
    END LOOP;
    
    RETURN v_loss;
END;
$$;

COMMENT ON FUNCTION train_step(BIGINT[], BIGINT, REAL) IS 
'Backpropagation training step: update relation weights based on prediction error.
Uses spatial distance as loss function. Reinforces correct predictions, weakens incorrect.
Online learning: trains on-the-fly as atoms are used.';
