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
        INSERT INTO atom_history (
            atom_id, content_hash, atom_value, canonical_text, spatial_key,
            reference_count, metadata, created_at, valid_from, valid_to
        ) VALUES (
            OLD.atom_id, OLD.content_hash, OLD.atom_value, OLD.canonical_text, OLD.spatial_key,
            OLD.reference_count, OLD.metadata, OLD.created_at, OLD.valid_from, now()
        );
        -- Update valid timestamps
        NEW.valid_from := now();
        RETURN NEW;
    ELSIF TG_OP = 'DELETE' THEN
        -- Archive deleted version
        INSERT INTO atom_history (
            atom_id, content_hash, atom_value, canonical_text, spatial_key,
            reference_count, metadata, created_at, valid_from, valid_to
        ) VALUES (
            OLD.atom_id, OLD.content_hash, OLD.atom_value, OLD.canonical_text, OLD.spatial_key,
            OLD.reference_count, OLD.metadata, OLD.created_at, OLD.valid_from, now()
        );
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
        INSERT INTO atom_composition_history (
            composition_id, parent_atom_id, component_atom_id, sequence_index,
            spatial_key, metadata, created_at, valid_from, valid_to
        ) VALUES (
            OLD.composition_id, OLD.parent_atom_id, OLD.component_atom_id, OLD.sequence_index,
            OLD.spatial_key, OLD.metadata, OLD.created_at, OLD.valid_from, now()
        );
        NEW.valid_from := now();
        RETURN NEW;
    ELSIF TG_OP = 'DELETE' THEN
        INSERT INTO atom_composition_history (
            composition_id, parent_atom_id, component_atom_id, sequence_index,
            spatial_key, metadata, created_at, valid_from, valid_to
        ) VALUES (
            OLD.composition_id, OLD.parent_atom_id, OLD.component_atom_id, OLD.sequence_index,
            OLD.spatial_key, OLD.metadata, OLD.created_at, OLD.valid_from, now()
        );
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
        INSERT INTO atom_relation_history (
            relation_id, source_atom_id, target_atom_id, relation_type_id,
            weight, confidence, importance, spatial_expression,
            metadata, last_accessed, created_at, valid_from, valid_to
        ) VALUES (
            OLD.relation_id, OLD.source_atom_id, OLD.target_atom_id, OLD.relation_type_id,
            OLD.weight, OLD.confidence, OLD.importance, OLD.spatial_expression,
            OLD.metadata, OLD.last_accessed, OLD.created_at, OLD.valid_from, now()
        );
        NEW.valid_from := now();
        RETURN NEW;
    ELSIF TG_OP = 'DELETE' THEN
        INSERT INTO atom_relation_history (
            relation_id, source_atom_id, target_atom_id, relation_type_id,
            weight, confidence, importance, spatial_expression,
            metadata, last_accessed, created_at, valid_from, valid_to
        ) VALUES (
            OLD.relation_id, OLD.source_atom_id, OLD.target_atom_id, OLD.relation_type_id,
            OLD.weight, OLD.confidence, OLD.importance, OLD.spatial_expression,
            OLD.metadata, OLD.last_accessed, OLD.created_at, OLD.valid_from, now()
        );
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
