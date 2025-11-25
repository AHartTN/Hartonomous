-- ============================================================================
-- TEMPORAL HISTORY TABLES
-- Complete audit trail for all three core tables
-- ============================================================================

-- Atom history (temporal versioning)
CREATE TABLE IF NOT EXISTS atom_history (
    LIKE atom INCLUDING ALL
);

COMMENT ON TABLE atom_history IS 'Historical versions of atoms for temporal queries and audit trail';

-- AtomComposition history
CREATE TABLE IF NOT EXISTS atom_composition_history (
    LIKE atom_composition INCLUDING ALL
);

COMMENT ON TABLE atom_composition_history IS 'Historical versions of compositions for temporal queries';

-- AtomRelation history
CREATE TABLE IF NOT EXISTS atom_relation_history (
    LIKE atom_relation INCLUDING ALL
);

COMMENT ON TABLE atom_relation_history IS 'Historical versions of relations for temporal queries';
