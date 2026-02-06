---
name: modality-ingestion
description: Map any data modality (text, audio, images, code) into Hartonomous via Unicode → Atoms → Compositions → Relations. Use when adding support for new content types.
---

# Modality Ingestion

**All digital content → Unicode codepoints → Atoms → Compositions → Relations (intelligence)**

## Content Type Mapping

| Content | Unicode Extraction | Parser |
|---------|-------------------|--------|
| Text | Direct UTF-8 → codepoints | `Engine/src/ingestion/text_ingester.cpp` |
| Code | Tree-sitter AST → structure + symbols | `Engine/external/tree-sitter/` |
| Images | RGB decimals: `[255,128,64]` → digit atoms | Not yet implemented |
| Audio | PCM decimals: `0.123` → digit + punct atoms | Not yet implemented |
| Structured | JSON/XML → canonical Unicode serialization | Not yet implemented |

## Pipeline Steps

1. **Extract Unicode** — Parse content to codepoint stream
2. **Map to Atoms** — Lookup pre-seeded atoms (NEVER create new atoms)
3. **Create Compositions** — N-grams with BLAKE3 content-addressing, run-length encoded
4. **Detect Relations** — Co-occurrence → initial ELO rating
5. **Track Evidence** — `content_id` in `relation_evidence` for provenance/GDPR

## Two Storage Modes

| Mode | When | Requirement |
|------|------|-------------|
| **Dense** | Documents, user data, trusted content | Bit-perfect reconstruction via complete `composition_sequence` |
| **Sparse** | AI models, statistical patterns | Relationships only, reconstruction NOT needed |

Dense = 90-95% compression from deduplication.
Sparse = 10,000-100,000x compression from cross-model deduplication.

## Scripts
```bash
./scripts/linux/30-ingest-text.sh    # Text ingestion
./scripts/linux/20-ingest-mini-lm.sh # Model ingestion (sparse)
```