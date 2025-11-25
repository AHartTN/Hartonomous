-- ============================================================================
-- LISTEN/NOTIFY Async Provenance Sync
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- PostgreSQL LISTEN/NOTIFY = Service Broker equivalent
-- Zero-latency async: SQL writes, AGE dreams (processes lineage)
-- ============================================================================

-- Notification function: fires on atom insert
CREATE OR REPLACE FUNCTION notify_atom_created()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_payload JSONB;
BEGIN
    -- Build payload with atom details
    v_payload := jsonb_build_object(
        'atom_id', NEW.atom_id,
        'content_hash', encode(NEW.content_hash, 'hex'),
        'modality', NEW.metadata->>'modality',
        'created_at', NEW.created_at
    );
    
    -- Async notification (non-blocking)
    PERFORM pg_notify('atom_created', v_payload::TEXT);
    
    RETURN NEW;
END;
$$;

-- Trigger on atom insert
CREATE TRIGGER trg_atom_created_notify
    AFTER INSERT ON atom
    FOR EACH ROW
    EXECUTE FUNCTION notify_atom_created();

COMMENT ON FUNCTION notify_atom_created() IS 
'LISTEN/NOTIFY async provenance sync.
Fires notification when atom created ? AGE worker processes lineage asynchronously.
Zero latency penalty for operational writes.';
