-- Function: Flag suspicious content
CREATE OR REPLACE FUNCTION flag_content(
    p_content_hash BYTEA,
    p_content_type VARCHAR(20),
    p_user_id UUID,
    p_reason TEXT
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO content_validation (
        content_hash,
        content_type,
        validation_status,
        validation_reason,
        flag_count,
        flagged_by_users
    ) VALUES (
        p_content_hash,
        p_content_type,
        'flagged',
        p_reason,
        1,
        ARRAY[p_user_id]
    )
    ON CONFLICT (content_hash, content_type) DO UPDATE
    SET
        flag_count = content_validation.flag_count + 1,
        flagged_by_users = array_append(content_validation.flagged_by_users, p_user_id),
        validation_status = CASE
            WHEN content_validation.flag_count + 1 >= 3 THEN 'rejected'
            ELSE 'flagged'
        END,
        validation_reason = content_validation.validation_reason || E'\n' || p_reason;

    -- Auto-reject if flagged by 3+ users
    IF (SELECT flag_count FROM content_validation WHERE content_hash = p_content_hash) >= 3 THEN
        -- Mark as rejected, trigger cleanup
        RAISE NOTICE 'Content % auto-rejected due to multiple flags', encode(p_content_hash, 'hex');
    END IF;
END;
$$;

COMMENT ON FUNCTION flag_content IS 'Flag suspicious content, auto-reject if 3+ flags';