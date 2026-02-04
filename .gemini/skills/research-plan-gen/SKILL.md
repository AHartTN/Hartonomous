---
name: research-plan-gen
description: Generate recursive research plans and decompose complex problems using the Godel Engine's meta-reasoning framework. Use when facing 'unsolvable' tasks or large architectural changes.
---

# Research Plan Generation: Godel Meta-Reasoning

This skill utilizes the Godel Engine framework to break down complex, multi-modal tasks into solvable trajectories.

## The Meta-Reasoning Cascade

1.  **Direct Retrieval (Level 0)**: Can I solve this by matching existing hashes or trajectories?
2.  **Semantic Audit (Level 1)**: What Atoms, Compositions, and Relationships already exist in this domain?
3.  **Gap Analysis (Level 2)**: Identify the "Known Unknowns"—missing trajectories or ambiguous manifolds.
4.  **Recursive Decomposition (Level 3)**: Break the task into atomic sub-problems with clear prerequisites.
5.  **Execution Roadmap (Level 4)**: Sequence the sub-problems into a new trajectory for implementation.

## Agentic Application

When given an ambiguous architectural request:
1.  **Audit**: Use `codebase_investigator` to map existing symbols and math implementations.
2.  **Decompose**: Create a `ResearchPlan` consisting of multiple `SubProblem` entries.
    *   Example: "Refactor Marshalling for 128-bit IDs"
    *   Sub-Problem A: Update `interop_api.h` structs.
    *   Sub-Problem B: Implement `uint128` conversion in `NativeBindings.cs`.
    *   Sub-Problem C: Verify GIST index consistency.
3.  **Execute**: Address gaps in order, verifying each step with `mathematical-verification`.

## Strategy
Always lossy-reconstruct your plan before acting. Ensure the decomposition trajectory logically connects the current state to the vision.