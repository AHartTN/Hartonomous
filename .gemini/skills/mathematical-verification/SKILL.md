---
name: mathematical-verification
description: Validate the geometric and topological integrity of the Hartonomous engine. Use when you need to verify Hilbert curve indices (strictly 128-bit), S³ projections, or Borsuk-Ulam antipodal relations.
---

# Mathematical Verification

This skill provides procedural rigor for validating the cascading trajectories and 128-bit geometric indexing.

## Strict Constraints

### 1. 128-Bit Hilbert Indices
The spatial index MUST be exactly 128 bits, representing a 4D Hilbert curve at 32-bit-per-dimension precision.
- **Storage**: Two 64-bit unsigned integers (`hi`, `lo`).
- **Domain**: `uint128` (implemented as 16-byte `bytea` in SQL).
- **Parity**: **Even IDs** are reserved for Compositions/Relations (sequenced trajectories); **Odd IDs** are reserved for base Atoms.

### 2. Unit-Norm Consistency
All points (Atoms) and centroids (Compositions/Relations) must reside on the S³ surface.
- **Verification**: $\|v\| = 1.0 \pm 1e-9$.
- **Tooling**: `Eigen::Vector4d::norm()`.

### 3. Manifold Gradient Continuity
Scalar properties (temperature, speed, etc.) are modeled as continuous trajectories.
- **The Rule**: Antonyms or distal property values (e.g., "Boiling" vs "Frozen") are distant nodes on a shared manifold, not necessarily antipodal.
- **Check**: Verify that intermediate states (e.g., "Warm") reside along the geodesic between distal anchors.
- **Continuity**: Small changes in property value must map to small geodesic distances on S³.

## Verification Workflow
1.  **Coordinate Audit**: Check `Physicality` table for normalization violations.
2.  **Parity Audit**: Ensure all trajectories (Level 1+) have even numeric identifiers.
3.  **Locality Check**: Validate that small movements in 4D space result in minimal jumps in the 128-bit Hilbert index.