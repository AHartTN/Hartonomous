---
name: spatial-s3-geometry
description: Implement 4D S³ (unit hypersphere) geometric operations. Use when modifying geometry code, implementing spatial operators, or verifying coordinate transformations.
---

# Spatial S³ Geometry: The Geometric Foundation

This skill covers the 4D unit hypersphere ($S^3$) that serves as the geometric substrate for Hartonomous intelligence.

## S³ Fundamentals

### Definition
The 3-sphere $S^3$ is the set of points in 4D space at unit distance from origin:

$$S^3 = \{(x, y, z, w) \in \mathbb{R}^4 : x^2 + y^2 + z^2 + w^2 = 1\}$$

**Properties**:
- **3-dimensional manifold** embedded in 4D space
- **Simply connected** (no holes)
- **Finite volume** but no boundary
- **Isomorphic to unit quaternions** (rotation group)

### Why S³?

1. **Uniform Geometry**: Every point is equivalent (no preferred location)
2. **Rich Structure**: 3D manifold provides more relationship capacity than 2D sphere
3. **Quaternion Connection**: Natural representation for rotations/orientations
4. **No Boundary**: Infinite traversal without edges (unlike cube or torus with boundaries)
5. **Spatial Indexing**: Hilbert curves on S³ → O(log N) lookups

## Core Geometric Operations

### 1. Geodesic Distance
**The shortest path between two points on S³.**

Implementation: `Engine/src/geometry/s3_distance.cpp`

```cpp
// Numerically stable formula
double geodesic_distance_s3(
    double x1, double y1, double z1, double w1,
    double x2, double y2, double z2, double w2
) {
    // Dot product
    double dot = x1*x2 + y1*y2 + z1*z2 + w1*w2;
    
    // Clamp to [-1, 1] for numerical stability
    dot = std::clamp(dot, -1.0, 1.0);
    
    // Geodesic distance = arccos(dot product)
    return std::acos(dot);
}

// Alternative numerically stable version:
double geodesic_distance_s3_stable(const double* p1, const double* p2) {
    // Compute ||p1 - p2||
    double diff_norm = 0.0;
    for (int i = 0; i < 4; ++i) {
        double diff = p1[i] - p2[i];
        diff_norm += diff * diff;
    }
    diff_norm = std::sqrt(diff_norm);
    
    // d = 2 * arcsin(||p1-p2|| / 2)
    return 2.0 * std::asin(diff_norm / 2.0);
}
```

**Range**: [0, π]
- 0: Identical points
- π/2: Orthogonal
- π: Antipodal (maximally distant)

### 2. Projection to S³
**Normalize any 4D vector to unit sphere surface.**

```cpp
void normalize_to_s3(double* coords) {
    double norm = std::sqrt(
        coords[0]*coords[0] + 
        coords[1]*coords[1] + 
        coords[2]*coords[2] + 
        coords[3]*coords[3]
    );
    
    if (norm < 1e-12) {
        // Degenerate case, use arbitrary point
        coords[0] = 1.0;
        coords[1] = 0.0;
        coords[2] = 0.0;
        coords[3] = 0.0;
    } else {
        coords[0] /= norm;
        coords[1] /= norm;
        coords[2] /= norm;
        coords[3] /= norm;
    }
}
```

### 3. Centroid on S³
**Average position of multiple points, projected back to surface.**

```cpp
void centroid_s3(const std::vector<double*>& points, double* out_centroid) {
    // Sum all points
    out_centroid[0] = 0.0;
    out_centroid[1] = 0.0;
    out_centroid[2] = 0.0;
    out_centroid[3] = 0.0;
    
    for (const auto& point : points) {
        out_centroid[0] += point[0];
        out_centroid[1] += point[1];
        out_centroid[2] += point[2];
        out_centroid[3] += point[3];
    }
    
    // Normalize to S³ surface
    normalize_to_s3(out_centroid);
}
```

**Note**: This is geometric mean, not intrinsic mean (which requires iterative computation).

### 4. Super Fibonacci Distribution
**Uniform seeding of ~1.114M Atoms on S³.**

Algorithm: Generalized Fibonacci lattice for S³

Implementation: `Engine/src/geometry/super_fibonacci.cpp`

```cpp
void super_fibonacci_s3(size_t n_points, std::vector<double*>& out_points) {
    // Golden ratio and plastic constant
    const double phi = (1.0 + std::sqrt(5.0)) / 2.0;  // 1.618...
    const double psi = std::pow(
        (9.0 + std::sqrt(69.0)) / 18.0, 
        1.0/3.0
    );  // Plastic constant, ~1.3247
    
    for (size_t i = 0; i < n_points; ++i) {
        double s = static_cast<double>(i) / n_points;
        
        // Spherical Fibonacci for first 2 coords
        double theta = 2.0 * M_PI * i / phi;
        double r = std::sqrt(s);
        
        double x = r * std::cos(theta);
        double y = r * std::sin(theta);
        
        // Extend to 4D using plastic constant
        double phi_3d = 2.0 * M_PI * i / psi;
        double r_remaining = std::sqrt(1.0 - s);
        
        double z = r_remaining * std::cos(phi_3d);
        double w = r_remaining * std::sin(phi_3d);
        
        out_points[i][0] = x;
        out_points[i][1] = y;
        out_points[i][2] = z;
        out_points[i][3] = w;
        
        normalize_to_s3(out_points[i]);
    }
}
```

**Result**: Approximately uniform coverage with low discrepancy.

### 5. Hopf Fibration (Optional Enhancement)
**Map S³ to S² (for visualization or alternative distribution).**

```cpp
void hopf_fibration(double x, double y, double z, double w, 
                     double* out_theta, double* out_phi) {
    // Project S³ -> S² via Hopf map
    double denom = 1.0 + w;
    if (std::abs(denom) < 1e-9) {
        denom = 1e-9;  // Avoid singularity
    }
    
    double u = x / denom;
    double v = y / denom;
    double s = z / denom;
    
    *out_theta = std::atan2(v, u);
    *out_phi = std::atan2(std::sqrt(u*u + v*v), s);
}
```

## Integration with PostGIS

### Storage Format
**4D coordinates stored as PostGIS `POINTZM`:**
- X, Y: Standard dimensions
- Z: Third spatial dimension
- M: Fourth dimension ("measure")

```sql
-- Create geometry column
ALTER TABLE hartonomous.physicality 
ADD COLUMN centroid geometry(POINTZM, 0);

-- Store S³ coordinate
UPDATE hartonomous.physicality
SET centroid = ST_MakePoint(x, y, z, w)
WHERE id = :id;
```

### Spatial Index
**GIST index for O(log N) queries:**

```sql
CREATE INDEX physicality_centroid_gist_idx 
ON hartonomous.physicality 
USING GIST (centroid);

-- Query using index
SELECT * FROM hartonomous.physicality
ORDER BY centroid <=> ST_MakePoint(0, 0, 0, 1)
LIMIT 10;
```

### Custom Distance Operator
**Register S³ geodesic distance as PostgreSQL operator:**

```sql
-- See postgres-extension-dev skill for implementation
CREATE OPERATOR <=> (
    LEFTARG = geometry,
    RIGHTARG = geometry,
    FUNCTION = s3_geodesic_distance
);
```

## Verification

### Unit Tests
```cpp
// test_geometry.cpp
TEST(S3Geometry, GeodesicDistance) {
    // Orthogonal points: distance should be pi/2
    double p1[4] = {1, 0, 0, 0};
    double p2[4] = {0, 1, 0, 0};
    double dist = geodesic_distance_s3(p1, p2);
    EXPECT_NEAR(dist, M_PI/2, 1e-9);
    
    // Antipodal points: distance should be pi
    double p3[4] = {-1, 0, 0, 0};
    dist = geodesic_distance_s3(p1, p3);
    EXPECT_NEAR(dist, M_PI, 1e-9);
}

TEST(S3Geometry, NormalizationPreservesDirection) {
    double v[4] = {1, 2, 3, 4};
    double direction[4] = {v[0], v[1], v[2], v[3]};
    
    normalize_to_s3(v);
    
    // Should maintain direction (parallel)
    double dot = v[0]*direction[0] + v[1]*direction[1] + 
                 v[2]*direction[2] + v[3]*direction[3];
    EXPECT_GT(dot, 0.0);  // Positive = same direction
    
    // Should be unit norm
    double norm = std::sqrt(v[0]*v[0] + v[1]*v[1] + 
                            v[2]*v[2] + v[3]*v[3]);
    EXPECT_NEAR(norm, 1.0, 1e-9);
}
```

### SQL Verification
```sql
-- All points should have norm = 1.0
SELECT COUNT(*) FROM hartonomous.physicality
WHERE ABS(
    SQRT(
        POW(ST_X(centroid), 2) + 
        POW(ST_Y(centroid), 2) + 
        POW(ST_Z(centroid), 2) + 
        POW(ST_M(centroid), 2)
    ) - 1.0
) > 1e-9;
-- Should return: 0
```

## Performance Considerations

- **SIMD**: Use AVX2/AVX-512 for vectorized distance computations
- **MKL**: BLAS operations for batch computations
- **Caching**: Frequently used centroids cached in memory
- **Spatial Index**: PostGIS GIST reduces query time from O(N) to O(log N)