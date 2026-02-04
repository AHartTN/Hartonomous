-- hartonomous--0.1.0.sql
-- This file should include only project schema SQL that belongs to your semantic substrate.

-- Use the schema for includes
\i 'domains/uint16.sql'
\i 'domains/uint32.sql'
\i 'domains/uint64.sql'
\i 'domains/uint128.sql'
\i 'domains/hilbert128.sql'
\i 'uint64_ops.sql'

-- Core Schema
\i 'tables/tenant.sql'
\i 'tables/user.sql'
\i 'tables/content.sql'
\i 'tables/physicality.sql'
\i 'tables/atom.sql'
\i 'tables/atom_metadata.sql'
\i 'tables/composition.sql'
\i 'tables/composition_sequence.sql'
\i 'tables/relation.sql'
\i 'tables/relation_sequence.sql'
\i 'tables/relation_rating.sql'
\i 'tables/relation_evidence.sql'
\i 'tables/audit_log.sql'

-- Diagnostic Views
\i 'views/v_promoted_units.sql'
\i 'views/v_semantic_neighbors.sql'
\i 'views/v_orphan_atoms.sql'