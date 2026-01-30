-- Function: Check rate limit before action
CREATE OR REPLACE FUNCTION check_rate_limit(
    p_tenant_id UUID,
    p_user_id UUID,
    p_action_type VARCHAR(50)
)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
DECLARE
    v_count_hour INTEGER;
    v_count_day INTEGER;
    v_max_hour INTEGER;
    v_max_day INTEGER;
BEGIN
    -- Get or create rate limit record
    INSERT INTO rate_limits (tenant_id, user_id, action_type)
    VALUES (p_tenant_id, p_user_id, p_action_type)
    ON CONFLICT (tenant_id, user_id, action_type) DO NOTHING;

    -- Reset counters if time expired
    UPDATE rate_limits
    SET
        count_last_hour = CASE WHEN NOW() >= hour_reset_at THEN 0 ELSE count_last_hour END,
        hour_reset_at = CASE WHEN NOW() >= hour_reset_at THEN NOW() + INTERVAL '1 hour' ELSE hour_reset_at END,
        count_last_day = CASE WHEN NOW() >= day_reset_at THEN 0 ELSE count_last_day END,
        day_reset_at = CASE WHEN NOW() >= day_reset_at THEN NOW() + INTERVAL '1 day' ELSE day_reset_at END
    WHERE tenant_id = p_tenant_id AND user_id = p_user_id AND action_type = p_action_type;

    -- Check limits
    SELECT count_last_hour, count_last_day, max_per_hour, max_per_day
    INTO v_count_hour, v_count_day, v_max_hour, v_max_day
    FROM rate_limits
    WHERE tenant_id = p_tenant_id AND user_id = p_user_id AND action_type = p_action_type;

    IF v_count_hour >= v_max_hour OR v_count_day >= v_max_day THEN
        RAISE EXCEPTION 'Rate limit exceeded for action %', p_action_type;
        RETURN FALSE;
    END IF;

    -- Increment counters
    UPDATE rate_limits
    SET
        count_last_hour = count_last_hour + 1,
        count_last_day = count_last_day + 1
    WHERE tenant_id = p_tenant_id AND user_id = p_user_id AND action_type = p_action_type;

    RETURN TRUE;
END;
$$;

COMMENT ON FUNCTION check_rate_limit IS 'Check and enforce rate limits per user/action';