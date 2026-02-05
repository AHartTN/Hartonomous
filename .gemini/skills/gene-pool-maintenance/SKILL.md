---
name: gene-pool-maintenance
description: Manage the immutable Unicode Atom foundation (~1.114M codepoints). Use when updating UCDIngestor, validating seed_unicode output, or troubleshooting Atom-level geometry.
---

# Gene Pool Maintenance: The Immutable Foundation

This skill manages the ~1.114M Unicode Atoms that form the unchanging geometric substrate for all intelligence.

## Atom Immutability Principle

**CRITICAL**: Atoms are seeded ONCE and become IMMUTABLE. All future intelligence builds on this locked foundation.

### 1. The Seeding Process
Atoms are populated by `seed_unicode` tool reading from `ucd_seed` database:
- **Source**: Unicode Character Database (UCD) + Unicode Collation Algorithm (UCA) via UCDIngestor.
- **Distribution**: Super Fibonacci + Hopf fibration for uniform S³ coverage.
- **Output**: `hartonomous.atom` table (~1.114M records) + `hartonomous.physicality` (S³ coordinates).
- **Tool**: `./build/linux-release-max-perf/Engine/tools/seed_unicode`
- **Script**: `./scripts/database/populate-atoms.sh`

### 2. Metadata Captured
- **Unicode Properties**: Category, age, block, name, decomposition, collation keys.
- **Geometric Position**: 4D S³ coordinates (x, y, z, w with ||v|| = 1.0).
- **Hilbert Index**: 128-bit spatial index for O(log N) lookups.
- **Content Hash**: BLAKE3 of codepoint for verification.

## Maintenance Workflow

### Normal Operation
**DO NOT** re-seed Atoms unless catastrophic database loss.
1.  Atoms are locked after initial population.
2.  All new content maps to existing Atoms.
3.  Intelligence emerges in Relations layer, NOT Atoms.

### Disaster Recovery
1.  **Drop Tables**: `DROP TABLE hartonomous.atom CASCADE;`
2.  **Recreate Schema**: `psql -U postgres -d hartonomous -f scripts/sql/01-core-tables.sql`
3.  **Re-Seed**: `./scripts/database/populate-atoms.sh`
4.  **Verify Count**: `SELECT COUNT(*) FROM hartonomous.atom;` → should be ~1,114,000
5.  **Verify Geometry**: All physicality.centroid should have norm = 1.0 ± 1e-9

### Unicode Version Updates
1.  Update UCDIngestor with new UCD/UCA data files.
2.  Regenerate `ucd_seed` database: `./UCDIngestor/setup_db.sh`
3.  **Full re-seed required** (incompatible with existing Relations).

## Validation
```sql
-- Count check
SELECT COUNT(*) FROM hartonomous.atom; -- ~1,114,000

-- Geometry check
SELECT COUNT(*) FROM hartonomous.physicality 
WHERE ABS(SQRT(POW(centroid[1],2) + POW(centroid[2],2) + 
              POW(centroid[3],2) + POW(centroid[4],2)) - 1.0) > 1e-9;
-- Should return 0
```