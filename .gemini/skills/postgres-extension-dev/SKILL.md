---
name: postgres-extension-dev
description: Develop PostgreSQL extensions (s3.so, hartonomous.so) that bridge Engine to database. Use when implementing spatial operators, GiST index methods, or SQL functions.
---

# PostgreSQL Extension Development

## Two Extensions

| Extension | Purpose | Links | Location |
|-----------|---------|-------|----------|
| **s3.so** | 4D S³ geometry + PostGIS integration | `engine_core` (math only) | `PostgresExtension/s3/` |
| **hartonomous.so** | Ingestion, query, graph navigation | `engine_io` (includes DB) | `PostgresExtension/hartonomous/` |

## Function Registration Pattern

**C++ side** (e.g., `PostgresExtension/s3/src/pg/s3_pg_shim.cpp`):
```cpp
extern "C" {
PG_MODULE_MAGIC;
PG_FUNCTION_INFO_V1(geodesic_distance_s3);

Datum geodesic_distance_s3(PG_FUNCTION_ARGS) {
    try {
        // Extract args, call Engine function, return result
        PG_RETURN_FLOAT8(distance);
    } catch (const std::exception& e) {
        ereport(ERROR, (errmsg("S3 error: %s", e.what())));
    }
}
}
```

**SQL side** (e.g., `PostgresExtension/s3/dist/s3--0.1.0.sql`):
```sql
CREATE FUNCTION geodesic_distance_s3(geometry, geometry) RETURNS float8
AS 'MODULE_PATHNAME' LANGUAGE C IMMUTABLE STRICT PARALLEL SAFE;
```

## Safety Rules
1. **Never let C++ exceptions cross into PostgreSQL** — wrap all function bodies in try/catch, convert to `ereport(ERROR, ...)`
2. **Never use malloc/free** — use PostgreSQL's `palloc/pfree`
3. **Always detoast arguments** — `PG_DETOAST_DATUM()` before accessing data
4. **Keep logic in C++ Engine** — extensions are thin shims, not business logic

## Build & Install
```bash
# Build (included in main cmake build)
cmake --build build/linux-release-max-perf --target s3 hartonomous_ext

# Install to PostgreSQL system dirs (requires sudo)
./scripts/linux/02-install.sh

# Or use dev symlinks (one-time setup, then sudo-free rebuilds)
./scripts/linux/02-install-dev-symlinks.sh

# Load in database
psql -U postgres -d hartonomous -c "CREATE EXTENSION s3;"
psql -U postgres -d hartonomous -c "CREATE EXTENSION hartonomous;"
```

## Debugging
```bash
# Check symbol exports
nm -D build/linux-release-max-perf/PostgresExtension/s3/s3.so | grep geodesic
# Check library dependencies
ldd build/linux-release-max-perf/PostgresExtension/s3/s3.so
# PostgreSQL logs for crash investigation
sudo tail -f /var/log/postgresql/postgresql-18-main.log
```