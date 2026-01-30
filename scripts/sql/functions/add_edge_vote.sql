-- Function: Add vote to semantic edge (updates ELO + provenance)
CREATE OR REPLACE FUNCTION add_edge_vote(
    p_source_hash BYTEA,
    p_target_hash BYTEA,
    p_edge_type VARCHAR(50),
    p_tenant_id UUID,
    p_user_id UUID,
    p_weight DOUBLE PRECISION,
    p_model_name VARCHAR(255) DEFAULT NULL
)
RETURNS INTEGER -- Returns new ELO rating
LANGUAGE plpgsql
AS $$
DECLARE
    new_elo INTEGER;
    vote_elo INTEGER;
BEGIN
    -- Convert weight to ELO
    vote_elo := 1500 + CAST(500.0 * (2.0 * p_weight - 1.0) AS INTEGER);

    -- Update or insert edge
    INSERT INTO semantic_edges (
        source_hash,
        target_hash,
        edge_type,
        elo_rating,
        usage_count,
        provenance,
        created_at,
        last_used_at
    ) VALUES (
        p_source_hash,
        p_target_hash,
        p_edge_type,
        vote_elo,
        1,
        jsonb_build_array(
            jsonb_build_object(
                'tenant_id', p_tenant_id,
                'user_id', p_user_id,
                'model', p_model_name,
                'weight', p_weight,
                'elo', vote_elo,
                'timestamp', NOW()
            )
        ),
        NOW(),
        NOW()
    )
    ON CONFLICT (source_hash, target_hash, edge_type) DO UPDATE
    SET
        -- Update ELO as weighted average
        elo_rating = (
            semantic_edges.elo_rating * semantic_edges.usage_count + vote_elo
        ) / (semantic_edges.usage_count + 1),
        usage_count = semantic_edges.usage_count + 1,
        provenance = semantic_edges.provenance || jsonb_build_array(
            jsonb_build_object(
                'tenant_id', p_tenant_id,
                'user_id', p_user_id,
                'model', p_model_name,
                'weight', p_weight,
                'elo', vote_elo,
                'timestamp', NOW()
            )
        ),
        last_used_at = NOW()
    RETURNING elo_rating INTO new_elo;

    RETURN new_elo;
END;
$$;

COMMENT ON FUNCTION add_edge_vote IS 'Add a vote to semantic edge, update ELO, track provenance';