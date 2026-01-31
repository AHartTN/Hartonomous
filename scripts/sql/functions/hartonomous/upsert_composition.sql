-- ==============================================================================
-- UPSERT COMPOSITION: Update Metadata Only (Creation handled by C++ Batch Ingester)
-- ==============================================================================

CREATE OR REPLACE FUNCTION upsert_composition(
    p_text TEXT
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    target_id UUID;
BEGIN
    -- 1. Compute ID
    target_id := uuid_send(blake3_hash(p_text));

    -- 2. Check Existence
    IF EXISTS (SELECT 1 FROM Composition WHERE Id = target_id) THEN
        -- Update Metadata (Touched)
        UPDATE Composition
        SET ModifiedAt = CURRENT_TIMESTAMP
        WHERE Id = target_id;
        
        -- Ideally update ELO/Observations here if we had Context
    ELSE
        -- 3. Reject Creation
        RAISE EXCEPTION 'Composition not found. Use Batch Ingester to create new geometric atoms.';
    END IF;
END;
$$;

COMMENT ON FUNCTION upsert_composition IS 'Updates metadata for existing compositions. Cannot create new ones (requires C++ geometry Engine).';