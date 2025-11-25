-- ============================================================================
-- TEMPORAL VERSIONING TRIGGERS
-- Automatic history tracking for complete audit trail
-- ============================================================================

-- Atom temporal trigger function
CREATE OR REPLACE FUNCTION atom_temporal_trigger()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        -- Archive old version
        INSERT INTO atom_history SELECT OLD.*;
        -- Update valid timestamps
        NEW.valid_from := now();
        RETURN NEW;
    ELSIF TG_OP = 'DELETE' THEN
        -- Archive deleted version
        INSERT INTO atom_history SELECT OLD.*;
        RETURN OLD;
    END IF;
END;
$$ LANGUAGE plpgsql;

-- Attach trigger to atom table
DROP TRIGGER IF EXISTS atom_temporal ON atom;
CREATE TRIGGER atom_temporal
    BEFORE UPDATE OR DELETE ON atom
    FOR EACH ROW EXECUTE FUNCTION atom_temporal_trigger();

-- ============================================================================
-- AtomComposition temporal trigger
-- ============================================================================

CREATE OR REPLACE FUNCTION atom_composition_temporal_trigger()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        INSERT INTO atom_composition_history SELECT OLD.*;
        NEW.valid_from := now();
        RETURN NEW;
    ELSIF TG_OP = 'DELETE' THEN
        INSERT INTO atom_composition_history SELECT OLD.*;
        RETURN OLD;
    END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS atom_composition_temporal ON atom_composition;
CREATE TRIGGER atom_composition_temporal
    BEFORE UPDATE OR DELETE ON atom_composition
    FOR EACH ROW EXECUTE FUNCTION atom_composition_temporal_trigger();

-- ============================================================================
-- AtomRelation temporal trigger
-- ============================================================================

CREATE OR REPLACE FUNCTION atom_relation_temporal_trigger()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        INSERT INTO atom_relation_history SELECT OLD.*;
        NEW.valid_from := now();
        RETURN NEW;
    ELSIF TG_OP = 'DELETE' THEN
        INSERT INTO atom_relation_history SELECT OLD.*;
        RETURN OLD;
    END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS atom_relation_temporal ON atom_relation;
CREATE TRIGGER atom_relation_temporal
    BEFORE UPDATE OR DELETE ON atom_relation
    FOR EACH ROW EXECUTE FUNCTION atom_relation_temporal_trigger();

-- ============================================================================
-- COMMENTS
-- ============================================================================

COMMENT ON FUNCTION atom_temporal_trigger() IS 'Automatically archive atom versions on UPDATE/DELETE for complete audit trail';
COMMENT ON FUNCTION atom_composition_temporal_trigger() IS 'Automatically archive composition versions';
COMMENT ON FUNCTION atom_relation_temporal_trigger() IS 'Automatically archive relation versions';
