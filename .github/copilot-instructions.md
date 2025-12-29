# Copilot instructions (Hartonomous)

## What this repo is (and is not)
- Hartonomous replaces LLM inference with **PostgreSQL/PostGIS queries + native graph algorithms**. See `docs/AGENT_BRIEFING.md` and `docs/PARADIGM.md`.
- Do **not** propose: vector stores, RAG, embeddings-at-query-time, calling external LLM APIs. The “model” is the database.

## Big-picture architecture (keep boundaries)
- **Atoms → Compositions → Relationships → Storage** (overview in `docs/ARCHITECTURE.md`).
- Tier split is intentional:
  - **C# / SQL** orchestrate and expose UX/API.
  - **C++ (Hartonomous.Native)** does heavy lifting (graph traversal, A*, SIMD, parallelism).
  - **PostgreSQL + PostGIS** stores and answers indexed/spatial queries.
- Conventions: **no recursive CTEs, no cursors, no RBAR** for graph traversal; if traversal/pathfinding is needed, implement it in native.

## Key code locations (where to edit)
- Native C++ core + DB access: `Hartonomous.Native/src/**` (schema/DB helpers referenced in `docs/ARCHITECTURE.md`).
- Native public C API surface: `docs/API.md` (and native exports header under `Hartonomous.Native/src/api/**`).
- .NET interop layer (P/Invoke via source-generated marshalling): `Hartonomous.Core/Native/NativeInterop.cs`.
- Shared CLI/terminal command logic: `Hartonomous.Commands/**` (e.g., `IngestCommandHandler.cs`).
- CLI entrypoint/command registry: `Hartonomous.CLI/Program.cs`.
- Interactive REPL (Terminal): `Hartonomous.Terminal/Repl/**`.
- Distributed app host (Aspire): `Hartonomous.AppHost/AppHost.cs`.

## Aspire (distributed app host)
- `Hartonomous.AppHost` uses .NET Aspire (`Aspire.AppHost.Sdk`) to run multiple projects together (currently `Hartonomous.Web` + `Hartonomous.Worker`).
- Typical dev loop: run the AppHost and keep infra (Postgres/Redis) running via `docker-compose.yml`.

## Build, test, validate (use the repo scripts)
- Windows “do the right thing” build: `./build.ps1` (native CMake preset + .NET build, optional tests/seed).
  - Examples: `./build.ps1 -Mode Release -Test` or `./build.ps1 -Clean -Test -Seed`.
  - Native presets used by the script: `windows-clang-debug` / `windows-clang-release` (build output under `Hartonomous.Native/out/build/<preset>/bin`).
- Full end-to-end validation (drops/recreates DB + schema + native tests): `./validate.ps1`.
- .NET outputs are redirected under `artifacts/` via `Directory.Build.props`. Native libraries are copied into build outputs from `Hartonomous.Core/runtimes/<RID>/native` by `Directory.Build.targets`.

## Toolchain preference (this repo)
- Prefer **Clang + CMake presets + Ninja** for native builds (see `Hartonomous.Native/CMakePresets.json`: `windows-clang-*`, `linux-clang-*`, `macos-clang-*`).
- Keep MAUI scoped: `Hartonomous.Maui` is Windows/macOS-specific; the rest of the solution should remain cross-platform where practical.

## Local infra (expected defaults)
- Dev compose stack: `docker-compose.yml`
  - Postgres+PostGIS is exposed on **localhost:5433** (container 5432).
  - Redis is exposed on **localhost:6379**.
  - Schema is auto-applied via `Hartonomous.Native/sql` mounted into Postgres init.
- Common local startup: `docker compose up -d postgres redis` (or run `./validate.ps1`, which will start Postgres if needed).
- Connection config conventions:
  - Native tools/tests use `HARTONOMOUS_DB_URL` (e.g., `postgresql://hartonomous:hartonomous@localhost:5433/hartonomous`).
  - Containers/services often use `.NET` configuration keys like `ConnectionStrings__Postgres` / `ConnectionStrings__Redis` (see `docker-compose.yml`).

## Database + determinism invariants (don’t break)
- Content is **content-addressed** and must remain deterministic: same input → same NodeRef/hash; decode(encode(x)) must round-trip.
- Relationship writes are **idempotent and aggregated** (see `docs/ARCHITECTURE.md` upsert examples): duplicates merge, `obs_count` increments.
- Prefer bulk operations via PostgreSQL **COPY** from native for high throughput.

## Native library loading (common pitfall)
- .NET apps call native via `LibraryImport` in `Hartonomous.Core/Native/NativeInterop.cs`.
- On Windows, runtime looks for `Hartonomous.Native.dll`; the build script also deploys a `libHartonomous.Native.dll` copy for cross-platform naming.
- If you change native exports, update both the native header/API and the corresponding `LibraryImport` entrypoints.

## Footgun to watch for
- Some docs mention `HARTONOMOUS_DB` (e.g., `docs/API.md` / `README.md`), but the native code and scripts primarily use `HARTONOMOUS_DB_URL`. Prefer `HARTONOMOUS_DB_URL` when wiring anything native.
