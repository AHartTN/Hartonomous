-- Policy: Admins can see all content in their tenant
CREATE POLICY admin_access ON content_ownership
    FOR ALL
    USING (
        tenant_id = current_setting('app.current_tenant_id')::UUID
        AND (
            current_setting('app.current_user_role') = 'admin'
            OR user_id = current_setting('app.current_user_id')::UUID
        )
    );

COMMENT ON POLICY tenant_isolation ON content_ownership IS 'Isolate content by tenant, allow public content';