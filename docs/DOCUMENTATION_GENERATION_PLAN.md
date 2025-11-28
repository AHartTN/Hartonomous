# Hartonomous Documentation Generation Plan

## Status: IN PROGRESS
**Last Updated:** 2025-11-28  
**Archive Location:** `docs/archive/2025-11-28_050415/`  
**Total Archived Files:** 151 markdown files

---

## Executive Summary

This document outlines the systematic approach to regenerating accurate, comprehensive documentation for Hartonomous based on:
1. **Current codebase state** (actual implementation)
2. **Archived documentation** (historical context, lessons learned)
3. **Vision alignment** (what we're actually building)

### Key Principles

- **Truth over history**: Documentation reflects CURRENT state, not past iterations
- **No orphaned information**: Every doc has clear purpose and audience
- **Architecture-first**: Start with high-level concepts, drill down to implementation
- **Code as source of truth**: Documentation derived from actual code, not aspirations
- **Single source**: No contradictory information across documents

---

## Phase 1: Foundation Analysis (COMPLETED)

### 1.1 Archive Organization ?
- Moved 151 markdown files to `docs/archive/2025-11-28_050415/`
- Includes recovered deleted documentation
- Preserved complete historical context

### 1.2 Codebase Inventory ?

**Current Technology Stack:**
- **Primary Language:** Python 3.14
- **Database:** PostgreSQL 16 + PostGIS 3.4
- **Graph Database:** Neo4j 5.15
- **API Framework:** FastAPI + Uvicorn
- **Code Atomizer:** C# (.NET) - Roslyn/Tree-sitter
- **Containerization:** Docker + Docker Compose
- **Reverse Proxy:** Caddy 2.7.5

**Project Structure:**
```
Hartonomous/
??? api/                          # FastAPI application
?   ??? ingestion_endpoints.py
??? src/
?   ??? core/
?       ??? atomization/          # Atom creation & management
?       ??? compression/          # Multi-layer compression
?       ??? landmark/             # Spatial positioning
?       ??? spatial/              # Hilbert curves, geometric ops
?   ??? Hartonomous.CodeAtomizer.Api/  # C# microservice (Roslyn/Tree-sitter)
??? schema/                       # PostgreSQL DDL
??? docker/                       # Container configuration
??? tests/                        # Test suite
```

### 1.3 Core Concepts Identified ?

From archived vision and current code:

**Fundamental Architecture:**
1. **Atoms**: ?64-byte unique values (content-addressable, deduplicated)
2. **Compositions**: Hierarchical structures (molecules from atoms)
3. **Relations**: Semantic connections (knowledge graph)
4. **Spatial Indexing**: Hilbert curves for semantic proximity
5. **Landmark Projection**: Positional semantics without massive embeddings
6. **Multi-layer Compression**: Sparse encoding + delta + bit packing
7. **Provenance Tracking**: Neo4j graph of all derivations

**Key Differentiators:**
- No separate training phase (ingestion = learning)
- Multi-modal unified representation (text/code/images/audio)
- CPU-first inference (spatial queries, not matrix math)
- Geometric truth detection (clustering = confidence)
- Full explainability (provenance graphs)

---

## Phase 2: Documentation Structure Design

### 2.1 New Documentation Hierarchy

```
docs/
??? README.md                          # Project overview + quick nav
??? ARCHITECTURE.md                    # High-level system design
??? VISION.md                          # What we're building & why
?
??? getting-started/
?   ??? README.md                      # Navigation
?   ??? installation.md                # Local dev setup
?   ??? quick-start.md                 # 5-minute demo
?   ??? first-ingestion.md             # Tutorial: ingest first document
?   ??? first-query.md                 # Tutorial: semantic search
?
??? concepts/
?   ??? README.md                      # Core concepts overview
?   ??? atoms.md                       # What are atoms?
?   ??? compositions.md                # Hierarchical structures
?   ??? relations.md                   # Semantic connections
?   ??? spatial-semantics.md           # Hilbert curves, landmark projection
?   ??? compression.md                 # Multi-layer compression
?   ??? provenance.md                  # Neo4j tracking
?   ??? modalities.md                  # Multi-modal representation
?
??? architecture/
?   ??? README.md                      # Architecture overview
?   ??? database-schema.md             # PostgreSQL tables, indexes
?   ??? api-design.md                  # FastAPI endpoints
?   ??? code-atomizer.md               # C# microservice (Roslyn/Tree-sitter)
?   ??? spatial-indexing.md            # PostGIS, Hilbert implementation
?   ??? compression-pipeline.md        # Encoding/decoding flow
?   ??? ingestion-flow.md              # How data enters system
?   ??? query-flow.md                  # How queries are executed
?   ??? provenance-tracking.md         # Neo4j integration
?
??? deployment/
?   ??? README.md                      # Deployment options
?   ??? local-docker.md                # Docker Compose setup
?   ??? production-docker.md           # Production-ready config
?   ??? azure-deployment.md            # Azure-specific guide
?   ??? performance-tuning.md          # PostgreSQL optimization
?   ??? monitoring.md                  # Observability setup
?
??? development/
?   ??? README.md                      # Developer onboarding
?   ??? project-structure.md           # Code organization
?   ??? coding-standards.md            # Style guide
?   ??? testing.md                     # Test strategy
?   ??? contributing.md                # How to contribute
?   ??? troubleshooting.md             # Common issues
?
??? api-reference/
?   ??? README.md                      # API overview
?   ??? ingestion.md                   # Ingestion endpoints
?   ??? query.md                       # Query endpoints
?   ??? admin.md                       # Admin endpoints
?   ??? code-atomizer.md               # Code atomizer API
?   ??? examples.md                    # cURL/Python examples
?
??? research/
?   ??? README.md                      # Research topics
?   ??? spatial-semantics-theory.md    # Mathematical foundations
?   ??? hilbert-curves.md              # Space-filling curves
?   ??? compression-algorithms.md      # Encoding techniques
?   ??? geometric-truth.md             # Clustering = confidence
?   ??? benchmarks.md                  # Performance comparisons
?
??? archive/
    ??? 2025-11-28_050415/             # Historical documentation
        ??? [151 archived files]
```

### 2.2 Documentation Types & Templates

**Type 1: Conceptual (WHY)**
- Target: Non-technical stakeholders, new developers
- Focus: Ideas, principles, use cases
- Example: `VISION.md`, `concepts/*.md`

**Type 2: Architectural (WHAT)**
- Target: System architects, senior developers
- Focus: Component design, data flows, trade-offs
- Example: `ARCHITECTURE.md`, `architecture/*.md`

**Type 3: Implementation (HOW)**
- Target: Active developers, DevOps
- Focus: Code details, deployment, operations
- Example: `development/*.md`, `deployment/*.md`

**Type 4: Reference (LOOKUP)**
- Target: API consumers, integrators
- Focus: Endpoints, parameters, examples
- Example: `api-reference/*.md`

---

## Phase 3: Content Extraction Strategy

### 3.1 Source Priority Matrix

When conflicts arise, use this priority order:

| Source | Priority | Usage |
|--------|----------|-------|
| **Current code** | 1 (Highest) | Source of truth for implementation |
| **Docker configs** | 2 | Infrastructure setup, environment |
| **Recent archived docs** | 3 | Conceptual explanations, vision |
| **Older archived docs** | 4 | Historical context only |

### 3.2 Extraction Rules

**For each new document:**

1. **Identify topic scope** (e.g., "How Hilbert curves work")
2. **Search current code** for actual implementation
3. **Extract working examples** from code/tests
4. **Review archived docs** for conceptual explanations
5. **Reconcile discrepancies** (code wins)
6. **Add current state notes** (e.g., "As of v0.x...")
7. **Link to related docs** (cross-reference)

### 3.3 Content Validation Checklist

Every document must have:
- [ ] **Clear audience** (who is this for?)
- [ ] **Stated purpose** (what problem does this solve?)
- [ ] **Current version tag** (e.g., "Hartonomous v0.3")
- [ ] **Working code examples** (tested, not aspirational)
- [ ] **Links to source files** (GitHub line numbers)
- [ ] **Last updated date**
- [ ] **Next/Previous navigation** (where applicable)

---

## Phase 4: Key Documents to Generate

### Priority 1: Core Navigation (Start Here)

1. **`docs/README.md`**
   - Project overview
   - "What is Hartonomous?" (2 paragraphs)
   - Technology stack
   - Documentation map
   - Quick links

2. **`docs/VISION.md`**
   - Refined version of archived `project-vision.md`
   - Remove aspirational features not yet implemented
   - Add "Current State vs Future Vision" section
   - Emphasize geometric semantics + provenance

3. **`docs/ARCHITECTURE.md`**
   - System overview diagram
   - Three core tables (atom, atom_composition, atom_relation)
   - Technology stack explanation
   - Component interaction diagram
   - Links to deep-dive docs

### Priority 2: Getting Started

4. **`docs/getting-started/installation.md`**
   - Prerequisites
   - Docker Compose setup
   - Environment variables
   - Verification steps
   - Troubleshooting

5. **`docs/getting-started/quick-start.md`**
   - 5-minute walkthrough
   - Start containers
   - Ingest sample data
   - Run sample query
   - View results

6. **`docs/getting-started/first-ingestion.md`**
   - Detailed tutorial
   - Ingest text document
   - Inspect atom creation
   - View spatial positioning
   - Query Neo4j provenance

### Priority 3: Core Concepts

7. **`docs/concepts/atoms.md`**
   - What is an atom?
   - ?64-byte constraint
   - Content addressing
   - Deduplication
   - Reference counting
   - Code examples

8. **`docs/concepts/spatial-semantics.md`**
   - Landmark projection
   - Hilbert curves
   - Why 3D space?
   - Semantic proximity = geometric distance
   - No embeddings needed

9. **`docs/concepts/compression.md`**
   - Multi-layer strategy
   - Sparse encoding
   - Delta encoding
   - Bit packing
   - Compression results

### Priority 4: Implementation Details

10. **`docs/architecture/database-schema.md`**
    - Full DDL with explanations
    - Index strategy
    - Performance considerations
    - Temporal versioning

11. **`docs/architecture/ingestion-flow.md`**
    - Step-by-step data flow
    - Atomization process
    - Spatial positioning
    - Composition creation
    - Provenance tracking

12. **`docs/deployment/local-docker.md`**
    - Docker Compose deep dive
    - Service configuration
    - Volume management
    - Networking
    - Debugging

---

## Phase 5: Content Sources Mapping

### From Archive ? New Docs

| Archived Source | New Destination | Notes |
|----------------|----------------|-------|
| `recovered/WORKSPACE_ARCHITECTURE.md` | `docs/ARCHITECTURE.md` | Major refactor, align with current code |
| `recovered/docs/vision/project-vision.md` | `docs/VISION.md` | Remove unimplemented features |
| `docs/HILBERT-CURVES.md` | `docs/concepts/spatial-semantics.md` | Merge with landmark projection |
| `docs/CQRS-ARCHITECTURE.md` | `docs/architecture/api-design.md` | If still relevant |
| `docs/deployment/azure-production.md` | `docs/deployment/azure-deployment.md` | Update for current infra |
| `docs/development/IMPLEMENTATION.md` | `docs/development/project-structure.md` | Current code structure |
| Various status/audit reports | DISCARD | Historical only |

### From Code ? New Docs

| Code Source | New Destination | Content |
|------------|----------------|---------|
| `src/core/atomization/` | `docs/concepts/atoms.md` | Atomization explained |
| `src/core/compression/` | `docs/concepts/compression.md` | Compression strategies |
| `src/core/spatial/` | `docs/concepts/spatial-semantics.md` | Hilbert implementation |
| `api/ingestion_endpoints.py` | `docs/api-reference/ingestion.md` | API spec |
| `docker-compose.yml` | `docs/deployment/local-docker.md` | Service breakdown |
| `schema/*.sql` | `docs/architecture/database-schema.md` | Schema docs |

---

## Phase 6: Quality Assurance

### 6.1 Accuracy Validation

For each document:
1. **Code references accurate?** (Check line numbers, class names)
2. **Examples actually work?** (Run code snippets)
3. **Links resolve correctly?** (No 404s)
4. **Diagrams match reality?** (No outdated architecture)

### 6.2 Completeness Check

For the full documentation set:
- [ ] Can a new developer onboard with just these docs?
- [ ] Can someone deploy to production?
- [ ] Are all API endpoints documented?
- [ ] Is every major code module explained?
- [ ] Are common errors addressed?

### 6.3 Consistency Verification

Ensure:
- Terminology is consistent (e.g., always "atom" not "node")
- Code style matches conventions
- Links follow same pattern
- Version numbers align

---

## Phase 7: Maintenance Plan

### 7.1 Documentation Ownership

| Area | Owner | Update Trigger |
|------|-------|----------------|
| Core concepts | Architecture team | Major design changes |
| API reference | Backend team | New endpoints, breaking changes |
| Deployment | DevOps team | Infrastructure changes |
| Getting started | DevRel/Docs team | New features, user feedback |

### 7.2 Review Cadence

- **Per-commit**: API reference auto-generated
- **Weekly**: Check for broken links
- **Per-release**: Update version numbers, screenshots
- **Quarterly**: Architecture review, conceptual updates

### 7.3 Deprecation Strategy

When removing features:
1. Mark section as `[DEPRECATED]`
2. Add removal version (e.g., "Removed in v0.5")
3. Suggest alternative
4. Keep for 2 releases, then delete

---

## Phase 8: Execution Plan

### Week 1: Foundation (Priority 1)
- [ ] `docs/README.md`
- [ ] `docs/VISION.md`
- [ ] `docs/ARCHITECTURE.md`

### Week 2: Onboarding (Priority 2)
- [ ] `docs/getting-started/installation.md`
- [ ] `docs/getting-started/quick-start.md`
- [ ] `docs/getting-started/first-ingestion.md`

### Week 3: Concepts (Priority 3)
- [ ] `docs/concepts/README.md` (navigation)
- [ ] `docs/concepts/atoms.md`
- [ ] `docs/concepts/compositions.md`
- [ ] `docs/concepts/relations.md`
- [ ] `docs/concepts/spatial-semantics.md`

### Week 4: Implementation (Priority 4)
- [ ] `docs/architecture/database-schema.md`
- [ ] `docs/architecture/ingestion-flow.md`
- [ ] `docs/deployment/local-docker.md`

### Ongoing: API Reference + Research
- [ ] Auto-generate from OpenAPI spec
- [ ] Research papers (as needed)

---

## Appendix A: Writing Guidelines

### Voice & Tone
- **Technical but approachable**: Explain concepts clearly
- **Confident but honest**: "This works" vs "This is aspirational"
- **Precise**: Use exact terminology
- **Example-driven**: Code > abstract explanations

### Document Structure Template

```markdown
# [Document Title]

**Audience:** [Who should read this?]  
**Purpose:** [What will you learn?]  
**Prerequisites:** [What should you know first?]

---

## Overview
[2-3 paragraphs explaining the topic at a high level]

## Key Concepts
[Define important terms with examples]

## How It Works
[Detailed explanation with code/diagrams]

## Examples
[Working code snippets with expected output]

## Common Issues
[Troubleshooting tips]

## Related Documentation
- [Link to related doc 1]
- [Link to related doc 2]

---

**Last Updated:** YYYY-MM-DD  
**Version:** Hartonomous v0.x
```

### Code Examples Best Practices

```python
# ? BAD: Aspirational code that doesn't run
result = magic_ai_function(query)  # This will exist someday

# ? GOOD: Actual working code
import psycopg2
conn = psycopg2.connect(database="hartonomous", user="hartonomous")
cur = conn.cursor()
cur.execute("SELECT * FROM atom LIMIT 10")
print(cur.fetchall())
```

---

## Appendix B: Archived Documentation Analysis

**High-value content to preserve:**
- Vision document (refined)
- Hilbert curve explanations
- Spatial semantics theory
- Compression algorithm details
- Azure deployment guides (updated)

**Content to discard:**
- Status reports (ephemeral)
- Audit logs (historical)
- "AI Sabotage" notes (non-technical)
- Redundant deployment guides (consolidate)
- Outdated architecture diagrams

**Contradictions found:**
- Some docs mention "10D space", code uses 3D ? **Use 3D** (code wins)
- Some docs reference Apache AGE, compose uses Neo4j ? **Use Neo4j** (code wins)
- GPU mentions (PG-Strom) not in current compose ? **Mark as future** (code wins)

---

## Sign-Off

This plan represents the systematic approach to regenerating accurate, maintainable documentation that reflects Hartonomous's **current state** and **actual vision**.

**Approval Required From:**
- [ ] Project Lead (Architecture alignment)
- [ ] Technical Lead (Implementation accuracy)
- [ ] Documentation Owner (Quality standards)

**Once approved, proceed to Phase 8 execution.**

---

*This is a living document. Update as the project evolves.*
