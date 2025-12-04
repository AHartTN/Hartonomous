# Schema Migration Plan: atom_composition ? composition_ids

## Current State
- **OLD**: `atom_composition` table with `parent_atom_id`, `component_atom_id`, `sequence_index`
- **NEW**: `atom.composition_ids` BIGINT[] array storing child IDs directly
- **Status**: Schema migrated, but code/tests/docs not updated

## Impact Assessment

### ? Already Migrated
- `schema/core/tables/001_atom.sql` - Has `composition_ids` column
- `alembic/versions/033_fractal_composition.py` - Migration script exists
- `schema/core/functions/composition/reconstruct_atom.sql` - Uses `composition_ids`

### ? Still References OLD Schema (20+ files)
1. **Python Services** (8 files)
   - `api/routes/health.py`
   - `api/services/code_atomization/code_atomization_service.py`
   - `api/services/geometric_atomization/topology_crystallization.py`
   - `api/services/text_atomization/text_atomizer.py`
   - `api/services/db_bulk_operations.py`
   - `api/services/document_parser.py`
   - `api/workers/neo4j_sync.py`
   - `scripts/query_atom.py`

2. **SQL Schema Files** (7 files)
   - `schema/core/indexes/composition/idx_composition_parent.sql`
   - `schema/core/indexes/spatial/idx_composition_spatial.sql`
   - `schema/core/triggers/001_temporal_versioning.sql`
   - `schema/core/triggers/003_provenance_notify.sql`
   - `schema/optimizations/01_add_positive_constraints.sql`
   - `schema/999_examples.sql`
   - `schema/core/tables/004_history_tables.sql`

3. **C# Code Atomizer** (1 file)
   - `src/Hartonomous.CodeAtomizer.Core/Atomizers/TreeSitterAtomizer.cs`

4. **Tests** (3+ files)
   - `tests/integration/test_end_to_end.py`
   - `tests/sql/test_atom_composition.py`
   - `tests/unit/test_base_atomizer.py`
   - `src/ingestion/parsers/code_parser.py`

5. **Documentation** (archived - can ignore)
   - Vision docs reference old 3-table architecture

## Migration Strategy

### Phase 1: Fix Schema Initialization (CRITICAL - Blocks Docker)
- [x] Comment out broken `meta_relations.sql` functions
- [ ] Create or drop legacy `atom_composition` table indexes
- [ ] Update `004_history_tables.sql` to not reference `atom_composition`
- [ ] Fix `001_temporal_versioning.sql` trigger

### Phase 2: Update Core Services (HIGH Priority)
- [ ] Update `AtomFactory.create_trajectory()` to use `composition_ids` array
- [ ] Remove `atom_composition` INSERT statements
- [ ] Update reconstruction queries to use `unnest(composition_ids)`

### Phase 3: Update Tests (HIGH Priority)
- [ ] Rewrite `test_atom_composition.py` to use `composition_ids`
- [ ] Fix `test_end_to_end.py` queries
- [ ] Update unit tests

### Phase 4: Update C# Code Atomizer (MEDIUM Priority)
- [ ] Update C# models to not expect `atom_composition` table
- [ ] May need to keep compatibility layer temporarily

### Phase 5: Documentation (LOW Priority)
- [ ] Update architecture docs (can wait)
- [ ] Update README examples

## Query Translation Guide

### OLD Pattern (atom_composition table):
```sql
-- Insert composition
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES (123, 456, 0);

-- Query components
SELECT component_atom_id, sequence_index
FROM atom_composition
WHERE parent_atom_id = 123
ORDER BY sequence_index;

-- Reconstruct
SELECT string_agg(a.canonical_text, '' ORDER BY ac.sequence_index)
FROM atom_composition ac
JOIN atom a ON a.atom_id = ac.component_atom_id
WHERE ac.parent_atom_id = 123;
```

### NEW Pattern (composition_ids array):
```sql
-- Insert composition (UPDATE atom)
UPDATE atom
SET composition_ids = ARRAY[456, 789, 101]
WHERE atom_id = 123;

-- OR during INSERT
INSERT INTO atom (content_hash, composition_ids, is_stable, metadata)
VALUES (hash_value, ARRAY[456, 789, 101], TRUE, '{}'::jsonb);

-- Query components (unnest array)
SELECT atom_id, idx-1 AS sequence_index
FROM atom,
     unnest(composition_ids) WITH ORDINALITY AS t(atom_id, idx)
WHERE atom.atom_id = 123;

-- Reconstruct  
SELECT string_agg(a.canonical_text, '' ORDER BY idx)
FROM atom parent,
     unnest(parent.composition_ids) WITH ORDINALITY AS t(child_id, idx)
JOIN atom a ON a.atom_id = t.child_id
WHERE parent.atom_id = 123;
```

## Immediate Action Items

1. **Fix Docker Init** (DONE)
   - Commented out `meta_relations.sql` broken functions
   
2. **Drop/Update Composition Indexes** (TODO)
   - Remove indexes on non-existent `atom_composition` table
   - Or keep table as deprecated compatibility layer

3. **Fix Test Failures** (TODO)
   - Update `test_end_to_end.py` to not query `atom_composition`
   - Update `test_atom_composition.py` to use new schema

4. **Update AtomFactory** (TODO)
   - Ensure `create_trajectory()` uses `composition_ids`
   - Remove any `atom_composition` INSERT logic

## Decision: Keep atom_composition Table?

### Option A: Drop It (Clean Break)
- **Pros**: No confusion, forces migration
- **Cons**: Breaks C# code atomizer, breaks old queries

### Option B: Keep as View (Compatibility)
```sql
CREATE VIEW atom_composition AS
SELECT 
    atom_id AS parent_atom_id,
    child_id AS component_atom_id,
    idx-1 AS sequence_index
FROM atom,
     unnest(composition_ids) WITH ORDINALITY AS t(child_id, idx)
WHERE composition_ids IS NOT NULL;
```
- **Pros**: Backwards compatible, gradual migration
- **Cons**: Performance overhead, two ways to do same thing

### Recommendation: **Option B** - Create compatibility VIEW
This allows gradual migration without breaking everything at once.
