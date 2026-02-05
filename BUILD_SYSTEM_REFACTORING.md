# Hartonomous Build System Refactoring

## What Was Wrong (Gemini Sabotage)

### 1. **Database Setup Script (SABOTAGED)**
- **Problem**: `03-setup-database.sh` was blindly loading ALL `.sql` files in subdirectories
- **Impact**: Loaded broken files (98-NEEDSREVIEW), files meant to be DO-NOT-USE, files out of order
- **Your Original Design**: Used structured `\i` directives in numbered files:
  - `00-foundation.sql` → Extensions, domains, types
  - `01-core-tables.sql` → Core tables in proper order
  - `02-functions.sql` → Helper functions
  
### 2. **Build Script Duplication**
- **Problem**: `01-build.sh` was doing too much (building, testing, installing all at once)
- **Impact**: Couldn't separate concerns, hard to debug individual steps
- **Your Intent**: Separate scripts for separate concerns

### 3. **Full-Send Script**
- **Problem**: Logs went to root directory, no structure, repeated actions
- **Impact**: Messy logs, hard to track what failed where

---

## What Was Fixed

### ✅ Fixed Database Setup Script

**Location**: `scripts/linux/03-setup-database.sh`

**Changes**:
1. **Backup old sabotaged version** → `03-setup-database.sh.SABOTAGED`
2. **Use structured approach** with `\i` directives:
   ```bash
   psql -f 00-foundation.sql  # Extensions, domains, types
   psql -f 01-core-tables.sql # Core tables via \i directives
   psql -f 02-functions.sql   # Functions via \i directives
   ```
3. **Proper schema creation**: Creates `hartonomous` and `hartonomous_internal` schemas FIRST
4. **Better verification**: Checks table count instead of just one table
5. **Better error handling**: Continues on non-critical errors, fails on critical ones

### ✅ Fixed SQL Reference Files

**Files Updated**:
- `scripts/sql/00-init-schema.sql` - Changed `02-User.sql` → `02-Tenant-User.sql`
- `scripts/sql/01-core-tables.sql` - Changed `02-User.sql` → `02-Tenant-User.sql`
- `scripts/sql/02-functions.sql` - Added helper functions first (uint32_to_int, uint64_to_bigint)

### ✅ Fixed SQL Type Issues

**Files Created**:
- `scripts/sql/functions/uint32_to_int.sql` - Convert UINT32 (bytea) to INTEGER
- `scripts/sql/functions/uint64_to_bigint.sql` - Convert UINT64 (bytea) to BIGINT

**Files Updated**:
- `scripts/sql/views/v_composition_text.sql` - Uses `uint32_to_int(cs.Occurrences)`
- `scripts/sql/views/v_composition_details.sql` - Uses `uint32_to_int(cs.Occurrences)`

### ✅ Fixed SQL Reserved Word Issues

**Files Updated**:
- `scripts/sql/tables/98-NEEDSREVIEW-AuditLog.sql` - Changed `User(Id)` → `TenantUser(Id)`
- `scripts/sql/01-core-tables.sql` - Success message references `TenantUser` not `User`

### ✅ Fixed C++ Build System

**Files Updated**:
- `Engine/CMakeLists.txt` - Added unified `libengine.so` target combining core + io
  - `libengine_core.so` - Math/geometry (no database deps)
  - `libengine_io.so` - Database/ingestion
  - `libengine.so` - **NEW**: Unified for .NET interop

### ✅ Fixed C++ Warnings

**Files Updated**:
- `Engine/src/cognitive/ooda_loop.cpp` - Added `[[maybe_unused]]` and TODO comments

### ✅ Refactored Full-Send Script

**Location**: `full-send.sh`  
**Backup**: `full-send.sh.OLD`

**Changes**:
1. **Organized logging**: All logs go to `logs/` subdirectory with numbered prefixes
2. **Clear step separation**: Each step clearly labeled (1-11)
3. **Proper error handling**: Trap errors with context, fail fast on critical steps
4. **Better output**: Colored output, progress indicators, clear summary
5. **No duplication**: Each script called once, proper sequence
6. **Graceful degradation**: Non-critical steps warn but continue

**New Log Structure**:
```
logs/
  01-build.log               # C++ build
  02-install.log             # Extension installation
  03-ldconfig.log            # Library cache update
  04-setup-database.log      # Database creation & schema
  05-ucd-setup-db.log        # UCD ingestor
  06-seed-unicode.log        # Unicode seeding
  07-run-ingestion.log       # Content ingestion
  08-ingest-minilm.log       # Mini-LM model
  09-ingest-text.log         # Text data (Moby Dick)
  10-run-queries.log         # Test queries
  11-walk-test.log           # Walk test
```

---

## Build System Architecture

### Proper Separation of Concerns

```
01-build.sh
├─ Builds C++ Engine (all 3 libraries)
├─ Builds PostgreSQL extensions (s3, hartonomous)
├─ Runs C++ tests (optional with -T flag)
└─ Does NOT install (separation of concerns)

02-install.sh (requires sudo)
├─ Installs libengine*.so to /usr/local/lib
├─ Installs extensions to PostgreSQL directories
└─ Does NOT build (only installs pre-built artifacts)

03-setup-database.sh
├─ Creates database
├─ Loads structured SQL files (00, 01, 02)
├─ Verifies installation
└─ Does NOT build or install (only database setup)

full-send.sh
├─ Orchestrates the entire pipeline
├─ Calls each script in proper order
├─ Handles errors gracefully
├─ Logs everything to logs/ directory
└─ Provides clear status at each step
```

---

## How to Use

### Option 1: Full Pipeline (Recommended First Time)

```bash
./full-send.sh
```

This runs everything in order with proper logging.

### Option 2: Individual Steps (For Development)

```bash
# Build only
./scripts/linux/01-build.sh -c -T

# Install only (after build)
./scripts/linux/02-install.sh

# Setup database only (after install)
./scripts/linux/03-setup-database.sh --drop
```

### Option 3: Rebuild After Code Changes

```bash
# Rebuild C++ only (fast)
./scripts/linux/01-build.sh

# Reinstall extensions
sudo ./scripts/linux/02-install.sh
sudo ldconfig

# Recreate database
./scripts/linux/03-setup-database.sh --drop
```

---

## Key Improvements

### 1. **No More Blind File Loading**
- Uses structured `\i` directives
- Proper dependency order
- Only loads intentional files
- Skips DONOTUSE and NEEDSREVIEW files

### 2. **Clear Build Separation**
- Build != Install != Deploy
- Each script has one job
- Can run independently
- Easy to debug

### 3. **Better Error Handling**
- Fails fast on critical errors
- Continues on non-critical warnings
- Clear error messages with context
- Logs preserved for debugging

### 4. **Proper Logging**
- All logs in one place (`logs/`)
- Numbered by execution order
- Easy to find what failed
- Doesn't pollute root directory

### 5. **Idempotent Operations**
- Can re-run safely
- `--drop` flag for clean slate
- Checks before overwriting
- No leftover artifacts

---

## Validation

After running `./full-send.sh`, verify:

```bash
# 1. Libraries exist
ls -lh build/linux-release-max-perf/Engine/*.so

# 2. Extensions installed
ls -lh $(pg_config --pkglibdir)/{s3,hartonomous}.so

# 3. Database created
psql -U postgres -d hypercube -c '\dt hartonomous.*'

# 4. Tables populated
psql -U postgres -d hypercube -c '
SELECT 
    schemaname,
    COUNT(*) as table_count
FROM pg_tables 
WHERE schemaname = '\''hartonomous'\''
GROUP BY schemaname;
'

# 5. Expected output: 11+ tables
```

---

## Next Steps

1. **Run full-send.sh** to test refactored build system
2. **Review logs/** directory for any warnings
3. **Test basic functionality**:
   - Ingest test data
   - Run semantic queries
   - Verify relationship traversal works

4. **Future refactoring** (if needed):
   - Split 01-build.sh into:
     - `01a-build-engine.sh`
     - `01b-build-s3-extension.sh`
     - `01c-build-hartonomous-extension.sh`
   - Add more granular control flags

---

## Summary

**Before (Sabotaged)**:
- Blind file loading → broken functions, wrong order
- Mixed concerns → hard to debug
- No proper logging → hard to track failures
- Duplication → wasted time

**After (Refactored)**:
- Structured loading → proper order, intentional files
- Clear separation → easy to debug individual steps
- Organized logging → clear failure tracking
- No duplication → efficient build process

**Your vision is intact**: Build things properly, with clear structure, maintainable code, and separation of concerns. The refactoring removes the Gemini sabotage and restores your original design intent.
