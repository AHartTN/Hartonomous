<#
.SYNOPSIS
  DELETED - CMake presets now use standard PATH-based tool discovery.
  
.DESCRIPTION
  This script previously hardcoded Windows paths.
  Native builds now rely on:
    - cmake/ninja/clang being in PATH (user's environment)
    - vcpkg for C++ dependencies (no FetchContent bandwidth waste)
  
  If you need build tools:
    - Install CMake, Ninja, LLVM via your package manager
    - Ensure they're in PATH
    - CMake presets will find them automatically
#>

throw "This script is deprecated. Ensure cmake/ninja/clang are in PATH, then run: cmake --preset <preset-name>"
