---
name: postgres-extension-dev
description: Develop and maintain C/C++ PostgreSQL extensions (PGXS). Use when modifying 'PostgresExtension/' code, implementing GIST index methods, or registering custom operators.
---

# PostgreSQL Extension Development

This skill governs the bridge between the C++ Engine and the PostgreSQL database backend.

## Extension Architecture

### 1. The C++ to PG Shim
- **Extern "C" Boundary**: All functions called by Postgres must be wrapped in `extern "C"`.
- **FMGR Protocol**: Utilize `PG_FUNCTION_INFO_V1` and the standard `Datum` return type.
- **Detoasting**: Mandatory usage of `PG_DETOAST_DATUM` for all `bytea` or `geometry` arguments to prevent crashes on compressed data.

### 2. GIST Index for 4D S³
Implementation: `PostgresExtension/s3/src/pg/s3_pg_gist.cpp`
- **Consistent Method**: Implements Strategy 1 for KNN ordering using the `<=>` operator.
- **Distance Pruning**: Correctly prunes nodes during traversal to maintain logarithmic performance on petabyte-scale datasets.
- **BBox Logic**: Uses 4D bounding boxes (`S3GistBBox`) for index node containment.

## Workflow
1.  **Logic Implementation**: Update C++ logic in `Engine/src/geometry/`.
2.  **Shim Registration**: Register exported functions in `PostgresExtension/hartonomous/src/pg_wrapper.cpp`.
3.  **SQL Registration**: Update `.sql` files in the extension's `sql/` directory to create operators and operator classes.
4.  **Versioning**: Update `.control` files for schema changes.

## Safety Rules
- **Catch All Exceptions**: Rethrowing a C++ exception into the Postgres process space will cause a backend crash.
- **Palloc Only**: Use `palloc`/`pfree` for memory that persists across the Postgres function call.