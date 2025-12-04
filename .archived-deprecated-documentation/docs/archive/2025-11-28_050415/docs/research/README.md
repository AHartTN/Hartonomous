# ?? Research & Internal Notes

**Development research, commit history, and design decisions**

---

## ?? Internal Documentation

This section contains research notes, experimental code, and historical context **not intended for end users**.

**For user-facing documentation, see:**
- [Getting Started](../getting-started/) - Installation & tutorials
- [Architecture](../architecture/) - Technical deep dives
- [API Reference](../api-reference/) - Function documentation

---

## ?? Contents

### [Python Stack Research](python-stack-research.md)

**Complete research on FastAPI + psycopg3 best practices**

**Findings from Microsoft Learn:**
- AsyncConnectionPool (mandatory for production)
- FastAPI lifespan pattern (replaces @app.on_event)
- Background workers (AGE sync via LISTEN/NOTIFY)
- Dependency injection patterns
- Retry logic with exponential backoff

**Outcome:** Comprehensive implementation guide for v0.6.0 REST API.

[**Read full research ?**](python-stack-research.md)

---

### [Commit Messages](commit-messages/)

**Archive of Git commit history organized by version**

**Purpose:** Historical record of development decisions.

**Structure:**
```
commit-messages/
??? v0.1.0-commits.md  # Initial schema
??? v0.2.0-commits.md  # Basic functions
??? v0.3.0-commits.md  # CQRS architecture
??? v0.4.0-commits.md  # Spatial algorithms
??? v0.5.0-commits.md  # Vectorization & parallel
```

**Example entry:**
```markdown
## feat: Add vectorized image atomization

**Date:** 2025-01-15
**Author:** Anthony Hart
**Commit:** 8c771bc

### Changes
- Replaced FOR loop with bulk UNNEST + generate_series
- 100x performance improvement (5s ? 50ms for 1M pixels)
- Eliminated RBAR (Row-By-Agonizing-Row)

### Rationale
PostgreSQL processes set-based operations in parallel.
Vectorization enables 8-16 workers per query.

### Files Changed
- schema/core/functions/atomization/atomize_image_vectorized.sql
```

[**View commit archive ?**](commit-messages/)

---

### [Design Decisions](design-decisions/)

**Record of architectural choices and alternatives considered**

**Format:**
```markdown
## Decision: Use PostgreSQL (not custom database)

**Date:** 2024-09-01
**Status:** ? Accepted

### Context
Need database with spatial indexing, graph queries, and ML capabilities.

### Options Considered
1. Custom C++ database (full control)
2. MongoDB (flexible schema)
3. Neo4j (native graph)
4. PostgreSQL (extensions)

### Decision
PostgreSQL with PostGIS, AGE, PL/Python extensions.

### Rationale
- Mature, battle-tested
- All needed features via extensions
- Horizontal scaling (Citus)
- ACID transactions
- Open source

### Consequences
+ No reinventing database internals
+ Large community support
+ Enterprise adoption easier
- Slower than custom C++ (acceptable trade-off)
- Extension installation required
```

[**View design decisions ?**](design-decisions/)

---

### [Experiments](experiments/)

**Failed experiments and lessons learned**

**Examples:**
- Tried pure pgvector (insufficient for multi-modal)
- Tried synchronous CQRS (too slow, moved to async)
- Tried custom compression (Hilbert RLE better)

**Value:** Avoid repeating mistakes.

[**View experiments ?**](experiments/)

---

## ?? Purpose of This Section

### 1. **Historical Record**

Preserve context for future developers:
- Why decisions were made
- What alternatives were considered
- What didn't work and why

---

### 2. **Knowledge Transfer**

When new contributors join:
- Read commit messages ? understand evolution
- Read design decisions ? understand rationale
- Read experiments ? avoid known pitfalls

---

### 3. **Research Archive**

External research (e.g., MS Docs) organized for reuse:
- Python stack best practices
- PostgreSQL performance tuning
- CQRS pattern implementation

---

## ?? Contributing to Research

### Adding Commit Messages

**After each version release:**

1. Create `commit-messages/vX.X.X-commits.md`
2. Extract significant commits:
```bash
git log v0.4.0..v0.5.0 --oneline --no-merges > v0.5.0-commits.txt
```
3. Format with context and rationale

---

### Documenting Design Decisions

**When making architectural choice:**

1. Create `design-decisions/YYYY-MM-DD-decision-name.md`
2. Use template:
   - Context
   - Options considered
   - Decision
   - Rationale
   - Consequences

---

### Recording Experiments

**When experiment fails:**

1. Create `experiments/YYYY-MM-DD-experiment-name.md`
2. Document:
   - Hypothesis
   - Approach
   - Results
   - Why it failed
   - Lessons learned

**Value:** Failures teach more than successes.

---

## ?? Visibility

### Public (GitHub)

- ? Commit messages (sanitized)
- ? Design decisions (redacted if needed)
- ? Internal experiments (too sensitive)

### Private (Internal Only)

- Proprietary research
- Competitive analysis
- Customer data
- Financial models

---

## ?? Navigation

### For Developers

**Start here:**
1. [Design Decisions](design-decisions/) - Understand why
2. [Commit Messages](commit-messages/) - See evolution
3. [Experiments](experiments/) - Learn from failures

**Then:**
- [Architecture](../architecture/) - Technical details
- [API Reference](../api-reference/) - Implementation

---

### For Researchers

**Start here:**
1. [Python Stack Research](python-stack-research.md) - External research
2. [Experiments](experiments/) - What didn't work

**Then:**
- [Cognitive Physics](../architecture/cognitive-physics.md) - Theoretical foundation
- [Vision](../vision/) - Philosophical context

---

## ?? Statistics

### v0.5.0 Research Summary

**Commits:** 15 well-organized commits  
**Functions:** 80+ implemented  
**Docs:** 23 files (17 migrated, 6 new)  
**External Research:** 2 comprehensive MS Docs searches  
**Design Decisions:** 5 major architectural choices  
**Experiments:** 3 failed, 2 successful

---

## ?? Maintenance

### Weekly

- ? Update commit archive
- ? Add new design decisions
- ? Document experiments

### Monthly

- ? Review for outdated information
- ? Consolidate duplicate notes
- ? Archive old experiments

### Quarterly

- ? Major reorganization if needed
- ? Extract patterns into docs
- ? Publish insights as blog posts

---

## ?? Credits

**Research sources:**
- [Microsoft Learn](https://learn.microsoft.com/) - Python/Azure best practices
- [PostgreSQL Documentation](https://www.postgresql.org/docs/) - Database internals
- [Apache AGE Documentation](https://age.apache.org/) - Graph query patterns

**Contributors:**
- Anthony Hart (primary author)
- GitHub Copilot (research assistant)

---

<div align="center">

**This documentation is for internal use**

For public documentation, see [main docs ?](../)

</div>
