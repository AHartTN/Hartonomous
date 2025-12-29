#!/usr/bin/env bash
set -euo pipefail

# Hartonomous Build Script - Cross-platform shell version
# Usage: ./build.sh [debug|release] [--clean] [--test] [--seed] [--skip-native]

MODE="Debug"
CLEAN=0
TEST=0
SEED=0
SKIP_NATIVE=0

# Parse arguments
for arg in "$@"; do
    case $arg in
        debug|Debug) MODE="Debug" ;;
        release|Release) MODE="Release" ;;
        --clean) CLEAN=1 ;;
        --test) TEST=1 ;;
        --seed) SEED=1 ;;
        --skip-native) SKIP_NATIVE=1 ;;
        *) echo "Unknown argument: $arg"; exit 1 ;;
    esac
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NATIVE_DIR="$ROOT/Hartonomous.Native"
CLI_DIR="$ROOT/Hartonomous.CLI"
TERMINAL_DIR="$ROOT/Hartonomous.Terminal"

# Detect platform
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    PRESET_DEBUG="linux-clang-debug"
    PRESET_RELEASE="linux-clang-release"
    RID="linux-x64"
    LIB_PREFIX="lib"
    LIB_EXT=".so"
    EXE_EXT=""
elif [[ "$OSTYPE" == "darwin"* ]]; then
    PRESET_DEBUG="macos-clang-debug"
    PRESET_RELEASE="macos-clang-release"
    RID="osx-x64"
    LIB_PREFIX="lib"
    LIB_EXT=".dylib"
    EXE_EXT=""
else
    echo "Unsupported platform: $OSTYPE"
    exit 1
fi

PRESET="${PRESET_RELEASE}"
if [[ "$MODE" == "Debug" ]]; then
    PRESET="${PRESET_DEBUG}"
fi

BUILD_DIR="$ROOT/artifacts/native/build/$PRESET"
LIB_NAME="${LIB_PREFIX}Hartonomous.Native${LIB_EXT}"
LIB_SOURCE="$BUILD_DIR/bin/$LIB_NAME"

# Color output
cyan() { echo -e "\033[36m$*\033[0m"; }
green() { echo -e "\033[32m$*\033[0m"; }
yellow() { echo -e "\033[33m$*\033[0m"; }
red() { echo -e "\033[31m$*\033[0m"; }

step() { cyan "\n=== $* ==="; }
success() { green "$*"; }
warning() { yellow "$*"; }

cyan "Hartonomous Build System"
cyan "========================"
echo "Mode: $MODE"
echo "Platform: $OSTYPE"
echo "Preset: $PRESET"

# Clean
if [[ $CLEAN -eq 1 ]]; then
    step "Cleaning build artifacts"
    rm -rf "$NATIVE_DIR/out"
    dotnet clean "$ROOT/Hartonomous.slnx" --configuration "$MODE" 2>/dev/null || true
    success "Clean complete"
fi

# Native build
if [[ $SKIP_NATIVE -eq 0 ]]; then
    step "Building Native Library (C++)"
    
    pushd "$NATIVE_DIR" >/dev/null
    cmake --preset "$PRESET"
    cmake --build "$BUILD_DIR" --config "$MODE" --parallel
    popd >/dev/null
    
    success "Native build complete: $LIB_SOURCE"
fi

# Deploy native library
step "Deploying Native Library to .NET Projects"

if [[ ! -f "$LIB_SOURCE" ]]; then
    red "Native library not found: $LIB_SOURCE"
    exit 1
fi

for PROJECT_DIR in "$CLI_DIR" "$TERMINAL_DIR"; do
    BIN_DIR="$PROJECT_DIR/bin/$MODE/net10.0/$RID"
    mkdir -p "$BIN_DIR"
    cp "$LIB_SOURCE" "$BIN_DIR/$LIB_NAME"
    echo "  -> $BIN_DIR/$LIB_NAME"
done

success "Native library deployment complete"

# .NET build
step "Building .NET Projects"

CORE_PROJECTS=(
    "Hartonomous.Core/Hartonomous.Core.csproj"
    "Hartonomous.Infrastructure/Hartonomous.Infrastructure.csproj"
    "Hartonomous.CLI/Hartonomous.CLI.csproj"
    "Hartonomous.Terminal/Hartonomous.Terminal.csproj"
    "Hartonomous.Worker/Hartonomous.Worker.csproj"
)

for proj in "${CORE_PROJECTS[@]}"; do
    proj_path="$ROOT/$proj"
    if [[ -f "$proj_path" ]]; then
        echo "Building $proj..."
        dotnet build "$proj_path" --configuration "$MODE"
    fi
done

success ".NET build complete"

# Copy native lib to artifacts output directories
for PROJECT_DIR in "$CLI_DIR" "$TERMINAL_DIR"; do
    ARTIFACT_BIN_DIR="$ROOT/artifacts/bin/$(basename $PROJECT_DIR)/${MODE,,}"
    if [[ -d "$ARTIFACT_BIN_DIR" && -f "$LIB_SOURCE" ]]; then
        cp "$LIB_SOURCE" "$ARTIFACT_BIN_DIR/"
        echo "Copied native lib to $ARTIFACT_BIN_DIR"
    fi
done

# Tests
if [[ $TEST -eq 1 ]]; then
    step "Running Native Tests"
    
    TEST_EXE="$BUILD_DIR/bin/hartonomous-tests${EXE_EXT}"
    if [[ -f "$TEST_EXE" ]]; then
        "$TEST_EXE"
        success "Native tests passed"
    else
        warning "Native test executable not found: $TEST_EXE"
    fi
    
    step "Running .NET Tests"
    dotnet test "$ROOT/Hartonomous.slnx" --configuration "$MODE" --no-build
    success ".NET tests passed"
fi

# Seeding
if [[ $SEED -eq 1 ]]; then
    step "Seeding Database"
    
    SEED_EXE="$BUILD_DIR/bin/hartonomous-seed${EXE_EXT}"
    if [[ -f "$SEED_EXE" ]]; then
        export HARTONOMOUS_DB_URL="${HARTONOMOUS_DB_URL:-postgresql://hartonomous:hartonomous@localhost:5433/hartonomous}"
        "$SEED_EXE"
        success "Database seeding complete"
    else
        warning "Seeder not built (requires PostgreSQL dev libraries)"
    fi
fi

# Summary
echo ""
green "========================================"
green " BUILD SUCCESSFUL "
green "========================================"
echo ""
CLI_EXE_PATH="$ROOT/Hartonomous.CLI/bin/$MODE/net10.0/$RID/hartonomous${EXE_EXT}"
TERMINAL_EXE_PATH="$ROOT/Hartonomous.Terminal/bin/$MODE/net10.0/$RID/hartonomous-terminal${EXE_EXT}"
echo "Run CLI:      cd $ROOT/Hartonomous.CLI/bin/$MODE/net10.0/$RID && ./hartonomous${EXE_EXT} info"
echo "Run Terminal: cd $ROOT/Hartonomous.Terminal/bin/$MODE/net10.0/$RID && ./hartonomous-terminal${EXE_EXT}"
echo ""
