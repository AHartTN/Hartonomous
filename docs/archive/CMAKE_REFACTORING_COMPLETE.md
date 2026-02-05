# CMake Build System Refactoring - COMPLETED

## Overview

This documents the major refactoring of the CMake build system to eliminate duplicate compilation and establish a permanent install directory structure.

## Problems Solved

### 1. **Duplicate Compilation (CRITICAL)**

**Problem:** The `engine` target was recompiling all CORE_SOURCES and IO_SOURCES that were already compiled into `engine_core` and `engine_io` targets. This meant every source file was compiled 2-3 times!

**Solution:** Implemented OBJECT library pattern:
```cmake
# Compile sources ONCE into object files
add_library(engine_core_objs OBJECT ${CORE_SOURCES} ...)
add_library(engine_io_objs OBJECT ${IO_SOURCES} ...)

# Shared libraries use pre-compiled objects
add_library(engine_core SHARED $<TARGET_OBJECTS:engine_core_objs>)
add_library(engine_io SHARED $<TARGET_OBJECTS:engine_io_objs>)
add_library(engine SHARED 
    $<TARGET_OBJECTS:engine_core_objs>
    $<TARGET_OBJECTS:engine_io_objs>
)
```

**Result:** Each source file compiles exactly once, objects reused by all targets. Significant build time reduction!

### 2. **Build Directory Instability**

**Problem:** Development symlinks pointed to `build/linux-release-max-perf/` which gets wiped on clean builds, breaking symlinks.

**Solution:** Created permanent install directory structure:
```
Hartonomous/
  install/                          # Permanent deployment (user-owned)
    lib/
      libengine_core.so
      libengine_io.so
      libengine.so
      s3.so
      hartonomous.so
    include/
      hartonomous/                  # All headers
    share/
      postgresql/extension/         # Extension SQL/control files
```

**Workflow:**
1. Build: `./scripts/linux/01-build.sh` → artifacts in `build/`
2. Install: `./scripts/linux/01a-install-local.sh` → copies to `install/`
3. Symlink (once): `sudo ./scripts/linux/02-install-dev-symlinks.sh` → system dirs point to `install/`
4. Iterate: `./rebuild.sh` → rebuilds + installs + ldconfig

### 3. **Why Three Libraries?**

**Discovered:** The split into `engine_core` and `engine_io` was NOT "AI sabotage" - it's architecturally necessary:

- **s3.so** (PostgreSQL extension) → links `engine_core` (geometry/math only, no database deps)
- **hartonomous.so** (PostgreSQL extension) → links `engine_io` (database access)
- **.NET app** → needs `libengine.so` (unified, all symbols in one .so for P/Invoke)

The waste was in recompiling, not the split itself.

## Files Modified

### Engine/CMakeLists.txt
- Added OBJECT libraries for single-compilation pattern
- Removed redundant `target_compile_definitions`
- Added proper VERSION and SOVERSION properties
- Enhanced install targets with export support

### PostgresExtension/s3/CMakeLists.txt
- Added install rules for .so file
- Added install rules for .control and .sql files

### PostgresExtension/hartonomous/CMakeLists.txt
- Added install rules for .so file
- Added install rules for .control and .sql files

## Files Created

### scripts/linux/01a-install-local.sh
- Runs `cmake --install` to populate `install/` directory
- No sudo required - user owns install directory
- Shows summary of installed artifacts

### scripts/linux/02-install-dev-symlinks.sh (UPDATED)
- Now symlinks to `install/` instead of `build/`
- Survives clean builds
- One-time setup with sudo

### rebuild.sh (UPDATED)
- Now runs 3 steps: build → install-local → ldconfig
- Complete development workflow in one script
- Only ldconfig needs sudo

## Directory Structure

```
install/                          # ← Permanent, user-owned
├── lib/
│   ├── libengine_core.so
│   ├── libengine_io.so
│   ├── libengine.so
│   ├── s3.so
│   └── hartonomous.so
├── include/
│   └── hartonomous/              # All engine headers
└── share/
    └── postgresql/
        └── extension/
            ├── s3.control
            ├── s3--0.1.0.sql
            ├── hartonomous.control
            └── hartonomous--0.1.0.sql

System symlinks:
/usr/local/lib/libengine*.so → install/lib/libengine*.so
/usr/lib/postgresql/18/lib/*.so → install/lib/*.so
/usr/share/postgresql/18/extension/* → install/share/postgresql/extension/*
/usr/local/include/hartonomous → install/include/hartonomous
```

## Development Workflow

### One-Time Setup
```bash
# 1. Build everything
./scripts/linux/01-build.sh

# 2. Install to local directory (no sudo)
./scripts/linux/01a-install-local.sh

# 3. Create system symlinks (sudo once)
sudo ./scripts/linux/02-install-dev-symlinks.sh
```

### Daily Iteration
```bash
# Edit code
vim Engine/src/whatever.cpp

# Rebuild and deploy (one command!)
./rebuild.sh
# This runs: build → install-local → ldconfig
# Only ldconfig needs sudo

# Test
./scripts/linux/03-setup-database.sh --drop
# Run your tests
```

### Clean Build
```bash
./rebuild.sh --clean
# Symlinks still work - install/ is permanent!
```

## Performance Impact

**Before:**
- CORE_SOURCES: ~50 files compiled for `engine_core` target
- IO_SOURCES: ~30 files compiled for `engine_io` target
  - Same files compiled AGAIN for `engine` target
- **Total: ~80 files compiled twice = 160 compilations**

**After:**
- All sources compiled ONCE into object files
- Objects linked into 3 different .so files
- **Total: ~80 compilations**

**Estimated speedup: ~40-50% build time reduction**

## Benefits

✅ **No duplicate compilation** - OBJECT libraries compile once, link many  
✅ **Permanent install directory** - Clean builds don't break symlinks  
✅ **User-owned deployment** - No sudo for build/install (only ldconfig)  
✅ **Fast iteration** - `./rebuild.sh` and you're done  
✅ **Proper versioning** - Added VERSION/SOVERSION properties  
✅ **Export support** - Added CMake export targets for find_package  
✅ **Architecture validated** - Three-library split is correct (not AI sabotage)  

## Next Steps

- [x] Eliminate duplicate compilation (OBJECT libraries)
- [x] Create permanent install directory
- [x] Update symlink infrastructure
- [x] Update rebuild workflow
- [ ] Replace GLOB_RECURSE with explicit source lists
- [ ] Centralize compiler flags to separate module
- [ ] Create organized cmake/ structure (modules/, configs/, utils/)
- [ ] Add proper testing infrastructure
- [ ] Profile actual build time improvements

## Testing

```bash
# Test clean workflow
rm -rf build/ install/ .dev-symlinks-active

# Build and setup
./scripts/linux/01-build.sh
./scripts/linux/01a-install-local.sh
sudo ./scripts/linux/02-install-dev-symlinks.sh

# Verify symlinks
ls -lh /usr/local/lib/libengine*
ls -lh $(pg_config --pkglibdir)/{s3,hartonomous,libengine}*

# Test iteration
touch Engine/src/geometry/s3_coord.cpp
./rebuild.sh

# Verify rebuild worked
ldd /usr/local/lib/libengine.so
```

## Notes

- `.gitignore` should include `install/` directory
- The three-library architecture is CORRECT - don't consolidate
- OBJECT libraries are the right pattern for this use case
- PostgreSQL extensions run in postgres process, .NET app is separate process
- Clean builds no longer break development workflow!
