---
name: modality-ingestion
description: Map any data modality (text, audio, images, code) into Hartonomous via Unicode → Atoms → Compositions → Relations. Use when adding support for new content types.
---

# Modality Ingestion: Universal Content Mapping

This skill governs conversion of any digital content into the geometric intelligence substrate.

## Universal Content Pipeline

**Core principle**: All digital content is Unicode. Unicode maps to Atoms. Atoms sequence to Compositions. Co-occurrence creates Relations (where intelligence emerges).

### 1. Unicode Extraction
Every content type resolves to Unicode codepoints.
- **Text**: Direct Unicode (UTF-8 → codepoints).
- **Code**: Via Tree-sitter AST → structure + symbols as Unicode.
- **Images**: RGB values as decimal digits: `[255, 128, 64]` → `"255"`, `"128"`, `"64"` → Unicode digit Atoms.
- **Audio**: PCM samples as decimal: `0.123` → `"0.123"` → Unicode digit + punctuation Atoms.
- **Structured Data**: JSON/XML → Unicode text via canonical serialization.

### 2. Atom Mapping
Unicode codepoints map to pre-existing Atoms (~1.114M immutable positions on S³).
- **Tool**: Lookup in `hartonomous.atom` table by codepoint.
- **Immutability**: Atoms are NEVER added after initial seed_unicode population.

### 3. Composition Creation
Sequences of Atoms (n-grams) become Compositions.
- **Storage**: `hartonomous.composition` with `hartonomous.composition_sequence` for ordering.
- **Content-Addressing**: BLAKE3 hash of Atom sequence for deduplication.
- **Run-Length Encoding**: Occurrences field for repeated sequences ("Mississippi" → "ss" stored once, occurs 2x).
- **Cascading Deduplication**: Compositions can reference other Compositions by hash (hierarchical).
- **Geometric Position**: Centroid of constituent Atom positions (normalized to S³ surface).

**CRITICAL**: For content requiring reconstruction (documents, user data), MUST store complete composition_sequence with correct ordering/positions to enable bit-perfect reconstruction.

### 4. Relation Detection (Intelligence Layer)
Co-occurring Compositions create Relations with initial ELO.
- **Storage**: `hartonomous.relation` + `hartonomous.relation_rating` (ELO score) + `hartonomous.relation_evidence` (provenance).
- **Intelligence**: Relations ARE the intelligence. ELO weights define semantic proximity, NOT geometric distance.
- **Evidence Tracking**: MUST record content_id (ingestion event) for surgical deletion capability.

## Two Storage Modes

### Dense Storage (Content Requiring Reconstruction)
**When:** Documents, images, audio files, user data, trusted content  
**Requirement:** Bit-perfect reconstruction via Relations → Compositions → Atoms → Unicode  
**Storage:**
- Complete composition_sequence with positions (NO gaps)
- Track in `hartonomous.content` table (ingestion event)
- 90-95% compression from deduplication

**Validation:**
```sql
-- Test reconstruction
SELECT reconstruct_content(content_id) FROM content WHERE source_identifier = 'test_file.txt';
-- Output hash MUST match input file hash
```

### Sparse Storage (Models/Statistical Patterns)
**When:** AI models, attention patterns, embeddings  
**Requirement:** Extract relationships only, reconstruction NOT needed  
**Storage:**
- Relation evidence entries (layer, head, weight, position)
- Deduplicate aggressively (same relation across 1000s of layers → ONE record)
- 10,000-100,000x compression from cross-model deduplication

**Example:** BERT has 12 layers × 12 heads = 144 matrices. Same relationship may appear in 50+ heads with different weights. Store as: ONE relation + 50 evidence entries.

## Implementation Workflow
1. **Parse Content**: Extract values using appropriate parser (Tree-sitter, PCM decoder, PNG reader, etc.).
2. **Map to Atoms**: Look up Unicode codepoints in existing Atom table.
3. **Determine Storage Mode**: Dense (reconstruction required) vs Sparse (extraction only).
4. **Create Compositions**: 
   - Dense: Insert ALL n-grams with positions for reconstruction
   - Sparse: Insert only relationship-forming n-grams
5. **Detect Relations**: Sliding window or co-occurrence analysis to find Composition pairs.
6. **Track Evidence**: Insert into `content` table for provenance + surgical deletion.
7. **Initialize ELO**: New relations start at base ELO (e.g., 1000), evolve through evidence aggregation.