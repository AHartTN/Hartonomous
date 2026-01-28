#!/usr/bin/env bash
# Hartonomous Build Script (Linux/macOS)
# Usage: ./build.sh [preset] [target]

set -e  # Exit on error

# Default values
PRESET="linux-release-max-perf"
TARGET=""
JOBS=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)
CLEAN=false
TEST=false
INSTALL=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

function print_success() {
    echo -e "${GREEN}$1${NC}"
}

function print_error() {
    echo -e "${RED}$1${NC}"
}

function print_info() {
    echo -e "${CYAN}$1${NC}"
}

function print_warning() {
    echo -e "${YELLOW}$1${NC}"
}

function show_help() {
    cat << EOF
Hartonomous Build Script

Usage: ./build.sh [OPTIONS]

Options:
  -p, --preset <name>   Build preset (default: release-native)
                        Available: release-native, release-portable, release-avx512,
                                  release-avx2, debug, relwithdebinfo
  -t, --target <name>   Specific target to build (default: all)
  -j, --jobs <N>        Number of parallel jobs (default: auto-detect)
  -c, --clean           Clean build directory before building
  -T, --test            Run tests after building
  -i, --install         Install after building
  -h, --help            Show this help message

Examples:
  ./build.sh                         # Build everything (release-native)
  ./build.sh --preset debug --test  # Debug build + run tests
  ./build.sh --clean --test          # Clean, build, test
  ./build.sh --target engine         # Build only engine
  ./build.sh --preset release-portable --install  # Build + install portable version

EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--preset)
            PRESET="$2"
            shift 2
            ;;
        -t|--target)
            TARGET="$2"
            shift 2
            ;;
        -j|--jobs)
            JOBS="$2"
            shift 2
            ;;
        -c|--clean)
            CLEAN=true
            shift
            ;;
        -T|--test)
            TEST=true
            shift
            ;;
        -i|--install)
            INSTALL=true
            shift
            ;;
        -h|--help)
            show_help
            ;;
        *)
            if [ -z "$TARGET" ]; then
                # First positional arg is preset
                PRESET="$1"
            else
                # Second positional arg is target
                TARGET="$1"
            fi
            shift
            ;;
    esac
done

print_info "=== Hartonomous Build Script ==="
echo ""

# Check dependencies
print_info "Checking dependencies..."

if ! command -v cmake &> /dev/null; then
    print_error "✗ CMake not found. Please install CMake 3.20+"
    exit 1
fi
CMAKE_VERSION=$(cmake --version | grep -oP 'version \K[0-9.]+' | head -1)
print_success "✓ CMake found: $CMAKE_VERSION"

# Check compiler
if command -v g++ &> /dev/null; then
    GCC_VERSION=$(g++ --version | grep -oP '\d+\.\d+\.\d+' | head -1)
    print_success "✓ GCC found: $GCC_VERSION"
elif command -v clang++ &> /dev/null; then
    CLANG_VERSION=$(clang++ --version | grep -oP 'version \K\d+\.\d+\.\d+' | head -1)
    print_success "✓ Clang found: $CLANG_VERSION"
else
    print_error "✗ No C++ compiler found (g++ or clang++)"
    exit 1
fi

# Check Intel OneAPI
if [ -n "$MKLROOT" ]; then
    print_success "✓ Intel OneAPI already initialized"
elif [ -f "/opt/intel/oneapi/setvars.sh" ]; then
    print_success "✓ Intel OneAPI found"
    source /opt/intel/oneapi/setvars.sh > /dev/null 2>&1
elif [ -f "/opt/intel/oneapi/mkl/latest/env/vars.sh" ]; then
    print_success "✓ Intel MKL found"
    source /opt/intel/oneapi/mkl/latest/env/vars.sh > /dev/null 2>&1
else
    print_warning "⚠ Intel OneAPI not found (will use system BLAS if available)"
fi

# Check submodules
echo ""
print_info "Checking submodules..."
SUBMODULES=(
    "Engine/external/blake3"
    "Engine/external/eigen"
    "Engine/external/hnswlib"
    "Engine/external/spectra"
    "Engine/external/json"
)

MISSING_SUBMODULES=()
for submodule in "${SUBMODULES[@]}"; do
    if [ -d "$submodule" ] && [ "$(ls -A $submodule)" ]; then
        print_success "✓ $submodule"
    else
        print_error "✗ $submodule (missing)"
        MISSING_SUBMODULES+=("$submodule")
    fi
done

if [ ${#MISSING_SUBMODULES[@]} -gt 0 ]; then
    echo ""
    print_warning "Missing submodules detected. Initializing..."
    git submodule update --init --recursive
fi

# Clean if requested
if [ "$CLEAN" = true ]; then
    echo ""
    print_info "Cleaning build directory..."
    BUILD_DIR="build/$PRESET"
    if [ -d "$BUILD_DIR" ]; then
        rm -rf "$BUILD_DIR"
        print_success "✓ Cleaned $BUILD_DIR"
    fi
    # Also clean .NET artifacts
    print_info "Cleaning .NET artifacts..."
    find . -type d -name "bin" -o -name "obj" | xargs rm -rf
    print_success "✓ Cleaned .NET artifacts"
fi

# Configure
echo ""
print_info "Configuring build (preset: $PRESET)..."
if cmake --preset "$PRESET"; then
    print_success "✓ Configuration complete"
else
    print_error "✗ Configuration failed"
    exit 1
fi

# Build C++
echo ""
BUILD_DIR="build/$PRESET"

if [ -n "$TARGET" ]; then
    print_info "Building target: $TARGET..."
    BUILD_CMD="cmake --build $BUILD_DIR --target $TARGET -j $JOBS"
else
    print_info "Building all C++ targets..."
    BUILD_CMD="cmake --build $BUILD_DIR -j $JOBS"
fi

if $BUILD_CMD; then
    print_success "✓ C++ Build complete"
else
    print_error "✗ C++ Build failed"
    exit 1
fi

# Build .NET
echo ""
print_info "Building .NET Solution..."
if command -v dotnet &> /dev/null; then
    print_info "Restoring dependencies..."
    if dotnet restore Hartonomous.sln; then
        print_success "✓ Restore complete"
    else
        print_error "✗ Restore failed"
        exit 1
    fi

    print_info "Building (Release)..."
    if dotnet build Hartonomous.sln -c Release --no-restore; then
        print_success "✓ .NET Build complete"
    else
        print_error "✗ .NET Build failed"
        exit 1
    fi
else
    print_error "✗ 'dotnet' command not found. Skipping .NET build (Installation required)."
    exit 1
fi

# Test
if [ "$TEST" = true ]; then
    echo ""
    print_info "Running tests..."
    if (cd "$BUILD_DIR" && ctest --output-on-failure); then
        print_success "✓ Tests passed"
    else
        print_error "✗ Tests failed"
        exit 1
    fi
fi

# Install
if [ "$INSTALL" = true ]; then
    echo ""
    print_info "Installing..."
    if sudo cmake --install "$BUILD_DIR"; then
        print_success "✓ Installation complete"
    else
        print_error "✗ Installation failed"
        exit 1
    fi
fi

echo ""
print_success "=== Build Complete ==="
echo ""
print_info "Build directory: $BUILD_DIR"
print_info "Preset: $PRESET"
if [ -n "$TARGET" ]; then
    print_info "Target: $TARGET"
fi
echo ""
print_info "Next steps:"
print_info "  1. Run tests: ./build.sh --test"
print_info "  2. Install: ./build.sh --install"
print_info "  3. Setup database: ./scripts/setup-database.sh"
echo ""
