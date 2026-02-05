---
name: cognitive-tuning
description: Optimize ELO-weighted graph navigation parameters for query strategies. Use when adjusting how the system explores relationship graph for specific reasoning goals.
---

# Cognitive Tuning: ELO Navigation Optimization

This skill provides parameters for optimizing relationship graph navigation in Hartonomous.

## Core Navigation Parameters

Intelligence = navigating ELO-weighted relationship paths. Parameters control exploration vs exploitation.

| Parameter | Role in Navigation |
| :--- | :--- |
| `temperature` | **Exploration**: Higher = more stochastic path selection (like transformer sampling). Lower = greedy high-ELO only. |
| `elo_threshold` | **Quality Filter**: Minimum ELO score to consider a relation. Prevents low-confidence paths. |
| `max_depth` | **Reasoning Depth**: How many relation hops from query. Deeper = more creative but slower. |
| `cross_modal_weight` | **Modality Fusion**: Bonus for paths bridging different content types (text↔image↔audio). |
| `novelty_bonus` | **Exploration Bias**: Reward for visiting underexplored relations (preventsループs). |

## Strategy Guides

### 1. Creative Exploration
When seeking non-obvious connections or cross-modal reasoning.
- `temperature`: 0.7-1.0 for stochastic path sampling.
- `novelty_bonus`: 0.3-0.5 to encourage new paths.
- `max_depth`: 5-10 hops for distant connections.
- `cross_modal_weight`: 1.5x to reward bridging modalities.

### 2. Precise Retrieval
When seeking established facts or high-confidence answers.
- `temperature`: 0.1-0.3 for greedy high-ELO selection.
- `elo_threshold`: 1000+ to filter weak relations.
- `max_depth`: 2-3 hops for direct connections.
- `cross_modal_weight`: 1.0x (no bonus).

## ELO Dynamics
Relation strength evolves through competition. Successful query paths increase ELO of traversed relations. Failed paths decrease ELO. System self-improves through usage.