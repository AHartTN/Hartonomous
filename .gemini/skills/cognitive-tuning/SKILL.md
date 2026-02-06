---
name: cognitive-tuning
description: Configure WalkEngine graph navigation parameters. Use when adjusting query exploration vs exploitation tradeoffs.
---

# Cognitive Tuning

## Navigation Parameters

| Parameter | Effect | Low Value | High Value |
|-----------|--------|-----------|------------|
| `temperature` | Path selection randomness | Greedy (high-ELO only) | Stochastic (creative) |
| `elo_threshold` | Minimum relation quality | Include weak relations | High-confidence only |
| `max_depth` | Relation hops from query | Direct connections | Distant/creative paths |
| `cross_modal_weight` | Bonus for modality bridging | No bonus | Reward cross-modal paths |
| `novelty_bonus` | Reward for unexplored relations | Stick to known paths | Explore new territory |

## Presets

**Precise retrieval**: `temperature=0.1, elo_threshold=1000, max_depth=3`
**Creative exploration**: `temperature=0.8, novelty_bonus=0.4, max_depth=8`

## Implementation
- WalkEngine: `Engine/src/cognitive/walk_engine.cpp`
- Parameters passed via `hartonomous_walk_step()` in `interop_api.h`
- ELO evolves through usage: successful paths increase, failed paths decrease