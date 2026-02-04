---
name: cognitive-tuning
description: Optimize Walk Engine parameters (weights, energy, temperature) for specific navigation goals. Use when you need to adjust how the system explores the semantic-geometric cascade.
---

# Cognitive Tuning: Trajectory Optimization

This skill provides the levers for optimizing the generative navigation of the Hartonomous DAG.

## Parameter Resonance

Tuning is about balancing the pull of high-level Relationships against the evidence of base Atoms.

| Parameter | Cascading Role |
| :--- | :--- |
| `w_model` | **Consensus**: External AI model intuition (MKL-accelerated KNN). |
| `w_text` | **Evidence**: Weight of decimal-atom sequencing (trajectories). |
| `w_rel` | **Trajectory**: Strength of Level 2 Relationship paths. |
| `w_geo` | **Physics**: Proximity on the S³ hypersphere. |
| `w_hilbert` | **Address**: Locality in the 128-bit geometric index. |

## Strategy Guides

### 1. High-Tortuosity Exploration
When the engine needs to find non-obvious semantic links.
- `energy`: Start high (2.0+) to allow "jumps" across S³.
- `w_novelty`: Increase to 0.4 to prevent local trajectory loops.
- `base_temp`: Higher (0.5+) for stochastic traversal.

### 2. Greedy Convergence (Convergent Thinking)
When pulling toward a specific goal node.
- `goal_attraction`: Increase to 5.0+.
- `energy_decay`: Increase to drain momentum as the goal is approached.
- `w_rel`: Priority 1. Follow the strongest existing Relationship trajectories.

## Parity-Aware Walk
Always bias the engine toward **Even** identifiers when seeking structured meaning (Compositions/Relations).