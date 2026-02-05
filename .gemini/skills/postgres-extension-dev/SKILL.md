---
name: postgres-extension-dev
description: Develop PostgreSQL extensions (s3.so, hartonomous.so) that bridge Engine to database. Use when implementing spatial operators, GIST index methods, or SQL functions.
---

# PostgreSQL Extension Development

This skill governs the PostgreSQL extension layer that exposes Engine capabilities as SQL operators and functions.

## Extension Architecture

### Two Extensions

1. **s3.so** (S³ Spatial Operations)
   - **Purpose**: Core 4D geometric operations + PostGIS integration
   - **Links**: `engine_core` (math only, no database)
   - **Provides**: Distance operators, spatial index (GIST), trajectory functions
   - **Location**: `PostgresExtension/s3/`

2. **hartonomous.so** (Full Intelligence Substrate)
   - **Purpose**: Ingestion, query execution, relationship graph operations
   - **Links**: `engine_io` (includes database integration)
   - **Provides**: Ingest functions, ELO updates, graph traversal
   - **Location**: `PostgresExtension/hartonomous/`

## C++ to PostgreSQL Bridge

### 1. Function Registration Pattern

**C++ Side** (`PostgresExtension/s3/src/pg/functions.cpp`):
```cpp
extern "C" {

// Required PostgreSQL macro
PG_MODULE_MAGIC;

// Geodesic distance function
PG_FUNCTION_INFO_V1(s3_geodesic_distance);

Datum s3_geodesic_distance(PG_FUNCTION_ARGS) {
    try {
        // Detoast arguments (CRITICAL for large/compressed data)
        bytea* coord_a_raw = PG_GETARG_BYTEA_P(0);
        bytea* coord_b_raw = PG_GETARG_BYTEA_P(1);
        
        bytea* coord_a = (bytea*)PG_DETOAST_DATUM(coord_a_raw);
        bytea* coord_b = (bytea*)PG_DETOAST_DATUM(coord_b_raw);
        
        // Extract 4D coordinates
        double* a = (double*)VARDATA(coord_a);
        double* b = (double*)VARDATA(coord_b);
        
        // Call Engine function
        double distance = hartonomous::geometry::geodesic_distance_s3(
            a[0], a[1], a[2], a[3],
            b[0], b[1], b[2], b[3]
        );
        
        // Free detoasted data if necessary
        if (coord_a != coord_a_raw) pfree(coord_a);
        if (coord_b != coord_b_raw) pfree(coord_b);
        
        PG_RETURN_FLOAT8(distance);
        
    } catch (const std::exception& e) {
        ereport(ERROR,
                (errcode(ERRCODE_INTERNAL_ERROR),
                 errmsg("S3 distance calculation failed: %s", e.what())));
    }
}

} // extern "C"
```

**SQL Side** (`PostgresExtension/s3/sql/s3--1.0.sql`):
```sql
-- Register function
CREATE OR REPLACE FUNCTION s3_geodesic_distance(bytea, bytea)
RETURNS double precision
AS 'MODULE_PATHNAME', 's3_geodesic_distance'
LANGUAGE C IMMUTABLE STRICT PARALLEL SAFE;

-- Create operator
CREATE OPERATOR <=> (
    LEFTARG = bytea,
    RIGHTARG = bytea,
    FUNCTION = s3_geodesic_distance,
    COMMUTATOR = <=>
);
```

### 2. GIST Index for 4D Spatial Queries

**Purpose**: O(log N) lookups for relationship graph navigation.

**Implementation** (`PostgresExtension/s3/src/pg/gist.cpp`):
```cpp
// GIST method structure
PG_FUNCTION_INFO_V1(s3_gist_consistent);
Datum s3_gist_consistent(PG_FUNCTION_ARGS) {
    // Strategy: 1 = KNN <=> operator
    StrategyNumber strategy = (StrategyNumber)PG_GETARG_UINT16(2);
    
    if (strategy == 1) {
        // Distance-based pruning for k-NN queries
        // ...
    }
    
    PG_RETURN_BOOL(result);
}

// Register operator class
```

**SQL Side** (`PostgresExtension/s3/sql/s3--1.0.sql`):
```sql
CREATE OPERATOR CLASS gist_s3_ops
DEFAULT FOR TYPE bytea USING gist AS
    OPERATOR 1 <=> (bytea, bytea) FOR ORDER BY float_ops,
    FUNCTION 1 s3_gist_consistent(internal, bytea, smallint, oid, internal),
    FUNCTION 2 s3_gist_union(internal, internal),
    FUNCTION 3 s3_gist_compress(internal),
    FUNCTION 5 s3_gist_penalty(internal, internal, internal),
    FUNCTION 6 s3_gist_picksplit(internal, internal),
    FUNCTION 7 s3_gist_same(bytea, bytea, internal),
    FUNCTION 8 s3_gist_distance(internal, bytea, smallint, oid, internal);
```

## Critical Safety Rules

### 1. Exception Handling
**NEVER let C++ exceptions cross into PostgreSQL**:
```cpp
PG_FUNCTION_INFO_V1(my_function);
Datum my_function(PG_FUNCTION_ARGS) {
    try {
        // All logic in try block
        return result;
    } catch (const std::exception& e) {
        ereport(ERROR, (...));  // Convert to PostgreSQL error
    } catch (...) {
        ereport(ERROR, (errmsg("Unknown error")));
    }
}
```

### 2. Memory Management
- **Never use `malloc/free`**: Use PostgreSQL's `palloc/pfree`
- **Detoast all arguments**: Compressed data will crash if accessed directly
- **Clean up on error**: PostgreSQL handles cleanup in transaction context

```cpp
// Good
bytea* data_raw = PG_GETARG_BYTEA_P(0);
bytea* data = (bytea*)PG_DETOAST_DATUM(data_raw);
// ... use data ...
if (data != data_raw) pfree(data);  // Free if detoasted

// Bad (will crash on compressed data)
bytea* data = PG_GETARG_BYTEA_P(0);
char* ptr = VARDATA(data);  // May be compressed!
```

### 3. Transaction Awareness
- Database connections passed to Engine must respect transaction state
- Don't hold locks across long operations
- ELO updates should be in transaction scope

## Build and Install

### Build Extensions
```bash
# Build both extensions
./scripts/build/build-all.sh

# Outputs:
# build/linux-release-max-perf/PostgresExtension/s3/s3.so
# build/linux-release-max-perf/PostgresExtension/hartonomous/hartonomous.so
```

### Install Extensions
```bash
# Install to project directory
./scripts/build/install-local.sh

# Symlink to PostgreSQL (ONE TIME)
sudo ./scripts/build/install-dev-symlinks.sh

# Load in database
psql -U postgres -d hartonomous -c "CREATE EXTENSION s3;"
psql -U postgres -d hartonomous -c "CREATE EXTENSION hartonomous;"
```

### Verify Installation
```sql
-- Check extensions loaded
SELECT * FROM pg_extension WHERE extname IN ('s3', 'hartonomous');

-- Test s3 functions
SELECT s3_geodesic_distance(
    '\x0000000000000000000000003FF0000000000000'::bytea,  -- [0,0,0,1]
    '\x3FF00000000000000000000000000000'::bytea            -- [1,0,0,0]
);
-- Should return: ~1.5708 (pi/2 for orthogonal points)

-- Check operator
SELECT * FROM hartonomous.physicality
ORDER BY centroid <=> :query_point
LIMIT 10;
```

## Debugging

### Symbol Not Found
```bash
# Check exports
nm -D build/linux-release-max-perf/PostgresExtension/s3/s3.so | grep s3_geodesic
# Should show: T s3_geodesic_distance

# Check dependencies
ldd build/linux-release-max-perf/PostgresExtension/s3/s3.so
# Should link libengine_core.so
```

### Backend Crash
```bash
# Check PostgreSQL log
sudo tail -f /var/log/postgresql/postgresql-18-main.log

# Look for:
# - AccessViolationException (detoast issue)
# - Uncaught exception (missing try/catch)
# - Memory corruption (palloc/pfree issue)
```

### Extension Version Mismatch
```sql
-- Drop and recreate
DROP EXTENSION s3 CASCADE;
CREATE EXTENSION s3;

-- Or update
ALTER EXTENSION s3 UPDATE TO '1.1';
```