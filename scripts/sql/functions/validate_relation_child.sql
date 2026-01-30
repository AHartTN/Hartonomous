-- Foreign key validation trigger (ensure child exists in correct table)
CREATE OR REPLACE FUNCTION validate_relation_child()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.child_type = 'composition' THEN
        -- Verify child exists in compositions table
        IF NOT EXISTS (SELECT 1 FROM compositions WHERE hash = NEW.child_hash) THEN
            RAISE EXCEPTION 'Composition child % does not exist', encode(NEW.child_hash, 'hex');
        END IF;
    ELSIF NEW.child_type = 'relation' THEN
        -- Verify child exists in relations table
        IF NOT EXISTS (SELECT 1 FROM relations WHERE hash = NEW.child_hash) THEN
            RAISE EXCEPTION 'Relation child % does not exist', encode(NEW.child_hash, 'hex');
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_relation_children_validate
BEFORE INSERT OR UPDATE ON relation_children
FOR EACH ROW
EXECUTE FUNCTION validate_relation_child();

COMMENT ON TABLE relation_children IS 'Join table for hierarchical relation sequences';