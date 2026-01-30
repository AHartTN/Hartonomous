-- Policy: Users can only insert content for their tenant
CREATE POLICY tenant_insert ON content_ownership
    FOR INSERT
    WITH CHECK (
        tenant_id = current_setting('app.current_tenant_id')::UUID
        AND user_id = current_setting('app.current_user_id')::UUID
    );