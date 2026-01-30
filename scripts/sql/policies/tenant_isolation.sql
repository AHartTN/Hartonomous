-- Policy: Users can see their own tenant's content
CREATE POLICY tenant_isolation ON content_ownership
    FOR SELECT
    USING (
        tenant_id = current_setting('app.current_tenant_id')::UUID
        OR is_public = TRUE
    );