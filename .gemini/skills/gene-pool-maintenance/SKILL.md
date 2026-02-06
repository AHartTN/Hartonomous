---
name: gene-pool-maintenance
description: Manage the immutable Unicode Atom foundation (~1.114M codepoints). Use when validating seed_unicode output, troubleshooting atom geometry, or handling Unicode version updates.
---

# Gene Pool Maintenance: The Immutable Foundation

Atoms are seeded ONCE from UCD/UCA data and become IMMUTABLE. All intelligence builds on this locked foundation.

## Seeding Pipeline

```
Engine/data/ucd/ (UCD XML + UCA collation + Unihan + confusables)
    ↓
UCDParser (SAX streaming)  →  Engine/src/unicode/ingestor/ucd_parser.cpp
    ↓
SemanticSequencer          →  Engine/src/unicode/ingestor/semantic_sequencer.cpp
  (category → script → UCA weight → radical/strokes → base codepoint)
    ↓
NodeGenerator              →  Engine/src/unicode/ingestor/node_generator.cpp
  (Halton sequences + Hopf fibration → deterministic S³ positions)
    ↓
UCDProcessor               →  Engine/src/unicode/ingestor/ucd_processor.cpp
  (orchestrate + bulk insert to hartonomous.atom + hartonomous.physicality)
```

**Entry point**: `Engine/tools/seed_unicode`
**Script**: `scripts/linux/05-seed-unicode.sh`
**Data**: `Engine/data/ucd/` (549MB, gitignored)

## Semantic Sequencing (Why Positioning Matters)

Atom S³ positions are NOT random — they encode semantic relationships:
1. **General Category** → primary S³ region (Letters cluster, Numbers cluster, etc.)
2. **Script** → sub-region (Latin together, CJK together)
3. **UCA Weights** (`allkeys.txt`) → ordering within scripts ('a' near 'b', 'A' near 'a')
4. **CJK Radical/Stroke** → CJK ideograph positioning (20K+ characters)
5. **Confusables** → visual similarity proximity (ℯ near e)

**This is the foundation of the geometric intelligence substrate. Bad positioning = garbage intelligence downstream.**

## Rules
- **Never re-seed** unless catastrophic database loss or Unicode version update
- **Never add atoms** after initial seeding — all content maps to existing atoms
- **Intelligence lives in Relations**, not in Atoms

## Validation
```sql
SELECT COUNT(*) FROM hartonomous.atom;  -- ~1,114,000
-- Geometry check: all centroids on S³ surface
SELECT COUNT(*) FROM hartonomous.physicality 
WHERE ABS(SQRT(POW(ST_X(centroid),2) + POW(ST_Y(centroid),2) + 
              POW(ST_Z(centroid),2) + POW(ST_M(centroid),2)) - 1.0) > 1e-9;
-- Should return 0
```

## Unicode Version Updates
1. Download new UCD/UCA files to `Engine/data/ucd/`
2. Full re-seed required (destroys existing Relations — they depend on atom positions)
3. `./scripts/linux/03-setup-database.sh --drop && ./scripts/linux/05-seed-unicode.sh`