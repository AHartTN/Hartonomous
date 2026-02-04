---
name: spatial-s3-geometry
description: Implement and verify 4D S³ (hypersphere) geometric operations. Use when modifying 'Engine/include/geometry/' or implementing 4D spatial operators in SQL.
---

# Spatial S³ Geometry

This skill covers the project's core mathematical substrate: digital meaning as trajectories on the 3-sphere ($S^3$).

## Core Geometric Principles

### 1. The S³ Manifold
- **Constraint**: $\sqrt{x^2 + y^2 + z^2 + w^2} = 1.0$.
- **Unit Quaternions**: Coordinates on S³ represent orientations in 4D, providing a mathematically rich space for semantic relationships.

### 2. Geodesic Distance ($d_{geodesic}$)
The distance between two concepts is the great-circle arc length on the hypersphere.
- **Formulation**: $d(u, v) = \arccos(u \cdot v)$.
- **Numerically Stable Fast-Core**: $d(u, v) = 2 \cdot \arcsin(\|u-v\|/2)$.
- **Implementation**: `Engine/src/geometry/s3_distance.cpp`.

### 3. Trajectory Analysis
- **Compositions as Trajectories**: A Composition is a 4D linestring connecting Atom-points.
- **Relationship Centroids**: Higher-level meanings are formed by calculating the normalized average (centroid) of trajectory points.
- **Tortuosity**: Semantic drift is measured as $\frac{\text{Total Arc Length}}{\text{Straight Line Distance}}$.

### 4. Super Fibonacci Distribution
- Used for uniform seeding of the 1.114M Unicode Atoms across S³.
- Employs the **Golden Ratio** and **Plastic Constant** for optimal discrepant distribution.

## SQL/PostGIS Integration
- **Type**: `POINTZM` (PostGIS geometry with Z and M components repurposed for 4D).
- **Operator**: `<=>` (Custom geodesic distance operator).
- **SRID**: 0 (Abstract unitless space).