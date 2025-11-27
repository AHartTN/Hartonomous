-- ============================================================================
-- Optimization: Add CHECK constraints for positive-only values
-- Impact: Query planner can optimize knowing values are positive
-- Cost: Minimal (constraint check on insert)
-- ============================================================================

-- Atom IDs are never negative
ALTER TABLE atom 
ADD CONSTRAINT atom_id_positive CHECK (atom_id > 0);

ALTER TABLE atom
ADD CONSTRAINT reference_count_nonnegative CHECK (reference_count >= 0);

-- Composition IDs
ALTER TABLE atom_composition
ADD CONSTRAINT composition_id_positive CHECK (composition_id > 0),
ADD CONSTRAINT parent_atom_id_positive CHECK (parent_atom_id > 0),
ADD CONSTRAINT component_atom_id_positive CHECK (component_atom_id > 0);

-- Relation IDs
ALTER TABLE atom_relation
ADD CONSTRAINT relation_id_positive CHECK (relation_id > 0),
ADD CONSTRAINT source_atom_id_positive CHECK (source_atom_id > 0),
ADD CONSTRAINT target_atom_id_positive CHECK (target_atom_id > 0),
ADD CONSTRAINT relation_type_id_positive CHECK (relation_type_id > 0);

COMMENT ON CONSTRAINT atom_id_positive ON atom IS
'Optimization: atom_id is always positive. Allows query planner to use positive-only range scans.';

COMMENT ON CONSTRAINT reference_count_nonnegative ON atom IS
'Optimization: reference_count cannot be negative. Enables query planner optimizations.';
