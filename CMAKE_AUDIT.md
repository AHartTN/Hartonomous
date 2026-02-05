# CMake Build System Audit & Refactoring Plan

## Current Issues Identified

### 1. **Inconsistent Naming & Organization**
- ❌ Mix of `-config.cmake` files in cmake/ directory
- ❌ Some are FindModules, some are Config files - naming doesn't reflect purpose
- ❌ No clear separation between third-party deps and project modules

### 2. **GLOB_RECURSE Antipattern**
```cmake
# Engine/CMakeLists.txt (CURRENT - BAD PRACTICE)
file(GLOB_RECURSE CORE_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/geometry/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/hashing/*.cpp"
    ...
)
```
**Problem**: CMake doesn't detect when new files are added, requires reconfigure  
**Solution**: Explicit source lists or use `cmake_path()` with proper detection

### 3. **Dependency Management Chaos**
```cmake
# Root CMakeLists.txt
include(cmake/mkl-config.cmake)      # Config file
include(cmake/eigen-config.cmake)    # Config file  
include(cmake/blake3-config.cmake)   # Build from source
include(cmake/postgis-config.cmake)  # System find
```
**Problem**: Inconsistent - some build from source, some find system, some are configs  
**Solution**: Unified approach with proper Find modules and Config files

### 4. **Target Duplication**
```cmake
# Engine/CMakeLists.txt creates 3 targets with overlapping sources:
add_library(engine_core ...)    # Core math/geometry
add_library(engine_io ...)      # Database/IO (depends on engine_core)
add_library(engine ...)         # DUPLICATE: Rebuilds CORE_SOURCES + IO_SOURCES
```
**Problem**: Recompiles sources multiple times, wastes build time  
**Solution**: Make `engine` an INTERFACE library that links core + io, OR make it the primary target

### 5. **Mixed Build Logic**
- Configuration options in root (good)
- But optimization flags in Engine/CMakeLists.txt function
- Some deps configured both in root and subdirs

### 6. **No Version Management**
- No project version numbers
- No SOVERSION for shared libraries
- No semantic versioning

### 7. **Installation Incomplete**
- Installs libraries but not properly configured
- No CMake config files generated for consumers
- No pkg-config files

### 8. **Platform-Specific Code Scattered**
```cmake
if(MSVC)
    # Windows stuff
else()
    # Unix stuff
endif()
```
Appears in multiple files instead of centralized

---

## Proposed New Structure

```
Hartonomous/
├── CMakeLists.txt                    # Root: Project definition, options, subdirs
├── CMakePresets.json                 # Build presets (already good)
├── cmake/
│   ├── HartonomousConfig.cmake.in    # Package config template
│   ├── modules/                      # Find modules for external deps
│   │   ├── FindMKL.cmake
│   │   ├── FindPostGIS.cmake
│   │   └── FindTreeSitter.cmake
│   ├── configs/                      # Config for built-from-source deps
│   │   ├── BLAKE3Config.cmake
│   │   ├── EigenConfig.cmake
│   │   └── HNSWConfig.cmake
│   ├── toolchains/                   # Toolchain files
│   │   ├── linux.cmake
│   │   └── windows.cmake
│   └── utils/                        # Helper functions/macros
│       ├── CompilerFlags.cmake       # Centralized compiler config
│       ├── InstallHelpers.cmake      # Installation helpers
│       └── SourceList.cmake          # Source management helpers
├── Engine/
│   ├── CMakeLists.txt                # Clean, uses above utilities
│   ├── src/
│   │   └── CMakeLists.txt            # Source lists per component
│   ├── include/
│   ├── tests/
│   │   └── CMakeLists.txt
│   └── tools/
│       └── CMakeLists.txt
├── PostgresExtension/
│   ├── CMakeLists.txt
│   ├── s3/
│   │   └── CMakeLists.txt
│   └── hartonomous/
│       └── CMakeLists.txt
└── UCDIngestor/
    └── CMakeLists.txt
```

---

## Refactoring Strategy

### Phase 1: Foundation (Utils & Modules)
1. ✅ Create `cmake/utils/` directory
2. ✅ Extract compiler flags to `CompilerFlags.cmake`
3. ✅ Create source list helpers
4. ✅ Reorganize dependency configs

### Phase 2: Root Cleanup
1. ✅ Clean up root CMakeLists.txt
2. ✅ Proper project versioning
3. ✅ Centralized option definitions
4. ✅ Better status messages

### Phase 3: Engine Refactoring  
1. ✅ Fix target duplication (choose one approach)
2. ✅ Replace GLOB_RECURSE with explicit lists
3. ✅ Clean separation of concerns
4. ✅ Proper target properties

### Phase 4: Extensions & Tools
1. ✅ Standardize extension build process
2. ✅ Clean UCDIngestor build
3. ✅ Proper dependencies

### Phase 5: Installation & Packaging
1. ✅ Generate CMake config files
2. ✅ Proper SOVERSION
3. ✅ Install rules for all artifacts
4. ✅ pkg-config files

---

## Key Principles

### 1. **Modern CMake** (Post-3.22)
- Use target-based approach (no global include_directories)
- Interface libraries for header-only deps
- Generator expressions for conditional compilation
- Proper transitive dependencies

### 2. **Explicit is Better Than Implicit**
- List sources explicitly (or use well-defined patterns)
- Explicit dependencies (PUBLIC/PRIVATE/INTERFACE)
- Clear target names with namespace aliases

### 3. **Separation of Concerns**
```cmake
# GOOD: Each file has one job
cmake/modules/FindMKL.cmake      # Finds MKL
cmake/utils/CompilerFlags.cmake  # Sets compiler flags
Engine/CMakeLists.txt            # Builds engine targets
```

### 4. **DRY (Don't Repeat Yourself)**
- Shared logic in functions/macros
- Centralized configuration
- Reusable patterns

### 5. **Discoverability**
- Consistent naming (Find*.cmake, *Config.cmake)
- Clear directory structure
- Good documentation in files

---

## Specific Refactoring Decisions

### Decision 1: Engine Library Structure

**Current (Wasteful)**:
```cmake
add_library(engine_core SHARED ${CORE_SOURCES})
add_library(engine_io SHARED ${IO_SOURCES})
add_library(engine SHARED ${CORE_SOURCES} ${IO_SOURCES})  # DUPLICATE!
```

**Option A: Interface Library** (Recommended)
```cmake
add_library(engine_core SHARED ${CORE_SOURCES})
add_library(engine_io SHARED ${IO_SOURCES})
add_library(engine INTERFACE)  # Links to core + io, no source duplication
target_link_libraries(engine INTERFACE engine_core engine_io)
```

**Option B: Single Target with Components**
```cmake
add_library(engine SHARED ${CORE_SOURCES} ${IO_SOURCES})
# .NET and tools link to engine
# Keep core/io as OBJECT libraries if separation needed later
```

**Recommendation**: Option A - Clean separation, no duplication, .NET links to INTERFACE target

### Decision 2: Source Lists

**Current (Bad)**:
```cmake
file(GLOB_RECURSE CORE_SOURCES "src/geometry/*.cpp" ...)
```

**Proposed (Good)**:
```cmake
# Engine/src/CMakeLists.txt
set(ENGINE_CORE_SOURCES
    geometry/s3_bbox.cpp
    geometry/s3_distance.cpp
    geometry/super_fibonacci.cpp
    hashing/blake3_pipeline.cpp
    # ... explicit list
    PARENT_SCOPE
)
```

### Decision 3: Dependency Management

**Current**: Mixed approaches  
**Proposed**: Standardized

```cmake
# For system-installed deps (MKL, PostGIS, PostgreSQL)
find_package(MKL REQUIRED COMPONENTS MKL::MKL)

# For vendored/submodule deps (BLAKE3, Eigen)
add_subdirectory(external/blake3)  # Provides BLAKE3::BLAKE3

# For header-only deps (Eigen, nlohmann/json)
add_library(Eigen3::Eigen INTERFACE IMPORTED)
```

---

## Implementation Priority

### HIGH PRIORITY (Do Now)
1. ✅ Fix engine target duplication
2. ✅ Create cmake/utils/ structure
3. ✅ Extract compiler flags
4. ✅ Clean root CMakeLists.txt

### MEDIUM PRIORITY (Next)
1. ⚠️ Replace GLOB_RECURSE with explicit lists
2. ⚠️ Standardize dependency finding
3. ⚠️ Add project versioning

### LOW PRIORITY (Later)
1. ⏳ Generate CMake config files
2. ⏳ Add installation rules
3. ⏳ Create pkg-config files

---

## Success Criteria

After refactoring, we should have:
- ✅ Clean, maintainable CMake code
- ✅ Fast, parallel builds
- ✅ No source duplication
- ✅ Consistent naming and structure
- ✅ Easy to add new sources
- ✅ Clear dependency management
- ✅ Proper installation support
- ✅ Good documentation

---

## Ready to Execute?

This audit shows significant issues but all are fixable. The refactoring will:
1. Make builds faster (no duplicate compilation)
2. Make code more maintainable (clear structure)
3. Make development easier (explicit source lists auto-detected)
4. Follow CMake best practices (modern target-based approach)

**Estimated Effort**: 2-3 hours of focused refactoring  
**Risk**: Low (can test after each change)  
**Benefit**: HIGH (clean foundation for future development)

Shall I proceed with the refactoring?
