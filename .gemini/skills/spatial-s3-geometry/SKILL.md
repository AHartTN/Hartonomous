---
name: spatial-s3-geometry
description: Implement 4D S³ (unit hypersphere) geometric operations. Use when modifying geometry code, implementing spatial operators, or verifying coordinate transformations.
---

# S³ Geometry

## Definition
S³ = {(x,y,z,w) ∈ ℝ⁴ : x² + y² + z² + w² = 1}

**Why S³**: Uniform geometry (no preferred location), rich 3D manifold, quaternion connection, no boundary, Hilbert curves for O(log N) spatial indexing.

## Core Operations

### Geodesic Distance
`Engine/src/geometry/s3_distance.cpp` — numerically stable via `acos(clamp(dot, -1, 1))`
- Range: [0, π] — 0=identical, π/2=orthogonal, π=antipodal

### Normalization
Project any 4D vector to S³ surface: `v' = v / ||v||`
- Degenerate case (||v|| < 1e-12): default to (1,0,0,0)

### Centroid
Average of points, normalized back to S³ surface (geometric mean, not intrinsic mean).

### Super Fibonacci Distribution
`Engine/src/geometry/super_fibonacci.cpp` — quasi-random S³ coverage using golden ratio (φ) + plastic constant (ψ). Used for initial atom seeding.

### Hopf Fibration
`Engine/include/geometry/hopf_fibration.hpp` — S³ → S² projection for visualization. Inverse: (S² point, fiber angle) → S³ point. Used in atom positioning pipeline.

## PostGIS Integration
- S³ coordinates stored as `POINTZM` (X, Y, Z, M=4th dimension)
- GiST index on `physicality.centroid` for O(log N) spatial queries
- Custom `<=>` operator via `s3.so` extension for geodesic distance

## Validation
```cpp
// All points must have unit norm ± 1e-9
double norm = sqrt(x*x + y*y + z*z + w*w);
assert(abs(norm - 1.0) < 1e-9);
```

## Files
| Operation | File |
|-----------|------|
| Geodesic distance | `Engine/src/geometry/s3_distance.cpp` |
| Super Fibonacci | `Engine/src/geometry/super_fibonacci.cpp` |
| Hopf fibration | `Engine/include/geometry/hopf_fibration.hpp` |
| Hilbert 4D index | `Engine/include/spatial/hilbert_curve_4d.hpp` |
| PostGIS operator | `PostgresExtension/s3/src/pg/s3_pg_shim.cpp` |