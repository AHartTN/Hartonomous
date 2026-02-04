---
name: gene-pool-maintenance
description: Manage the Unicode Gene Pool and UCDIngestor workflows. Use when updating Unicode versions or mapping new atom metadata (dt, dm, CE).
---

# Gene Pool Maintenance

This skill manages the immutable set of 1.114M seeded Unicode Atoms that form the foundation of all trajectories.

## Atom Foundations

### 1. Seeded Sovereignty
Atoms are fixed. You do not "add" atoms; you map new content to the existing 1.114M codepoint Gene Pool.
- **Source**: Unicode Character Database (UCD).
- **Table**: `ucd.code_points`.

### 2. Metadata Mapping
- **`dt` (Decomposition Type)**: Defines how atoms break down (canonical, font, super, etc.).
- **`dm` (Decomposition Mapping)**: The sequence of atoms that form the decomposition (the Level 0 trajectory).
- **`CE` (Composition Exclusion)**: Flags atoms that should remain independent primitives.

## Maintenance Workflow
1.  **Ingestion**: Execute `run_ingestion.sh` to update the `ucd` schema with the latest character metadata.
2.  **Parity Validation**: Ensure the seeding process respects the **Even-Composition / Odd-Atom** identifier rule.
3.  **Stability Check**: Verify that `hilbert_index` values for seeded Atoms remain stable across database re-seeding events.

## Trajectory Integrity
Meaning is meaningless until sequenced. `Gene Pool Maintenance` ensures that the base Atoms are semantically categorized (category, age, block) so that `Compositions` can inherit their initial spatial orientation.