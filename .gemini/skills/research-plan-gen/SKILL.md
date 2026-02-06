---
name: research-plan-gen
description: Decompose complex problems into executable sequences respecting Hartonomous architecture layers. Use for multi-step architectural changes.
---

# Research Plan Generation

## Task Decomposition Framework

1. **Check existing code**: `grep -r "feature_name" Engine/src/ Engine/tests/`
2. **Identify correct layer**: Atoms (immutable) → Compositions (sequences) → Relations (intelligence)
3. **Respect dependencies**: Build → DB → Extensions → Seed → Ingest → Query
4. **Break into atomic steps** with clear inputs/outputs

## Layer Rules
- **Geometry/math changes** → `Engine/src/geometry/`, `Engine/src/spatial/`
- **Storage/DB changes** → `Engine/src/storage/`, `scripts/sql/`
- **Ingestion pipeline** → `Engine/src/ingestion/`, `Engine/src/unicode/ingestor/`
- **Cognitive/reasoning** → `Engine/src/cognitive/`, `Engine/src/query/`
- **SQL interface** → `PostgresExtension/hartonomous/src/`
- **API layer** → `app-layer/`

## Example: "Add audio ingestion"
1. Parse PCM samples → numeric Unicode sequences (digits + decimal point atoms)
2. Map to existing Atoms (already seeded, immutable)
3. Create Compositions (n-grams with BLAKE3 content-addressing)
4. Detect co-occurrences → Relations with initial ELO
5. Track evidence → `relation_evidence` with `content_id` for provenance

**Always verify:** Does build pass? Do existing tests still pass?