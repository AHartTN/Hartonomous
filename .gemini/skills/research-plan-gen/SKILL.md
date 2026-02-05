---
name: research-plan-gen
description: Generate recursive research plans and decompose complex problems using structured meta-reasoning. Use when facing multi-step architectural changes or knowledge gaps in the relationship graph.
---

# Research Plan Generation: Structured Meta-Reasoning

This skill breaks down complex, multi-component tasks into executable sequences aligned with Hartonomous architecture.

## The Meta-Reasoning Framework

1.  **Direct Retrieval (Level 0)**: Can I solve this with existing code, schemas, or tools?
2.  **Component Audit (Level 1)**: What Atoms, Compositions, Relations already exist? What tools are available (seed_unicode, ingest_model, ingest_text)?
3.  **Gap Analysis (Level 2)**: Identify missing implementations, undefined schema elements, or empty regions in relationship graph (Mendeleev parallel).
4.  **Recursive Decomposition (Level 3)**: Break task into atomic sub-problems with clear dependencies.
5.  **Execution Roadmap (Level 4)**: Sequence sub-problems respecting build order, data dependencies, and architectural constraints.

## Agentic Application

When given an ambiguous architectural request:
1.  **Audit**: Use semantic_search and grep_search to map existing implementations across Engine/, PostgresExtension/, UCDIngestor/.
2.  **Decompose**: Create structured plan with clear dependencies.
    *   Example: "Add audio ingestion support"
    *   Sub-Problem A: Parse PCM samples to numeric sequences.
    *   Sub-Problem B: Map sequences to existing Atoms (digits, decimal point).
    *   Sub-Problem C: Create Compositions (n-grams of Atoms).
    *   Sub-Problem D: Detect co-occurrences → Relations with initial ELO.
3.  **Execute**: Address gaps in dependency order, validating each step.

## Strategy
Ensure plan respects architectural layers: Atoms (immutable) → Compositions (sequences) → Relations (intelligence). Changes must preserve geometric substrate integrity.