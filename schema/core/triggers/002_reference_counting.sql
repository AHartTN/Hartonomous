-- ============================================================================
-- REFERENCE COUNTING TRIGGERS
-- Conservation of reference: track atom usage
-- ============================================================================

-- Increment reference count when atom is composed
CREATE OR REPLACE FUNCTION increment_reference_count()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE atom
    SET reference_count = reference_count + 1
    WHERE atom_id = NEW.component_atom_id;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_increment_refcount ON atom_composition;
CREATE TRIGGER trigger_increment_refcount
    AFTER INSERT ON atom_composition
    FOR EACH ROW EXECUTE FUNCTION increment_reference_count();

-- Decrement reference count when composition is removed
CREATE OR REPLACE FUNCTION decrement_reference_count()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE atom
    SET reference_count = GREATEST(reference_count - 1, 0)
    WHERE atom_id = OLD.component_atom_id;
    
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_decrement_refcount ON atom_composition;
CREATE TRIGGER trigger_decrement_refcount
    AFTER DELETE ON atom_composition
    FOR EACH ROW EXECUTE FUNCTION decrement_reference_count();

-- ============================================================================
-- COMMENTS
-- ============================================================================

COMMENT ON FUNCTION increment_reference_count() IS 'Conservation of reference: increment atomic mass when composed';
COMMENT ON FUNCTION decrement_reference_count() IS 'Conservation of reference: decrement atomic mass when decomposed';
