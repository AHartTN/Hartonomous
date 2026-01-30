-- Policy: Users can update their own content
CREATE POLICY user_update ON content_ownership
    FOR UPDATE
    USING (user_id = current_setting('app.current_user_id')::UUID);