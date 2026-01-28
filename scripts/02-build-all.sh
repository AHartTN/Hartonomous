#!/usr/bin/env bash
# Script 2: Build All (C++ Engine + PostgreSQL Extension)

set -e

source "$(dirname "$0")/common.sh"

print_header "Building Hartonomous"

REPO_ROOT="$(get_repo_root)"
cd "$REPO_ROOT"

BUILD_TYPE=${1:-Release}

# Detect platform and choose appropriate preset
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "win32" ]] || uname -s | grep -q "MINGW\|MSYS\|CYGWIN"; then
    PRESET=${2:-windows-release-max-perf}
elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    PRESET=${2:-linux-release-max-perf}
elif [[ "$OSTYPE" == "darwin"* ]]; then
    print_error "macOS presets not yet configured in CMakePresets.json"
    exit 1
else
    PRESET=${2:-windows-release-max-perf}
fi

print_info "Platform: $(uname -s)"
print_info "Build type: $BUILD_TYPE"
print_info "Preset: $PRESET"

# Windows/MSVC: Setup Visual Studio environment
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "win32" ]] || uname -s | grep -q "MINGW\|MSYS\|CYGWIN"; then
    print_step "Detecting Visual Studio..."

    # Common VS 2022 locations
    VCVARSALL_PATHS=(
        "D:/Microsoft Visual Studio/2022/Community/VC/Auxiliary/Build/vcvarsall.bat"
        "C:/Program Files/Microsoft Visual Studio/2022/Community/VC/Auxiliary/Build/vcvarsall.bat"
        "C:/Program Files (x86)/Microsoft Visual Studio/2022/Community/VC/Auxiliary/Build/vcvarsall.bat"
        "D:/Program Files/Microsoft Visual Studio/2022/Professional/VC/Auxiliary/Build/vcvarsall.bat"
        "C:/Program Files/Microsoft Visual Studio/2022/Professional/VC/Auxiliary/Build/vcvarsall.bat"
    )

    VCVARSALL=""
    for path in "${VCVARSALL_PATHS[@]}"; do
        if [ -f "$path" ]; then
            VCVARSALL="$path"
            break
        fi
    done

    if [ -n "$VCVARSALL" ]; then
        print_success "Found Visual Studio: $VCVARSALL"
        print_info "Setting up VS environment..."

        # Run vcvarsall and capture environment
        cmd.exe /c "\"$VCVARSALL\" x64 > nul && set" > /tmp/vsenv.txt

        # Export PATH and other key variables
        while IFS='=' read -r key value; do
            case "$key" in
                PATH|INCLUDE|LIB|LIBPATH)
                    export "$key=$value"
                    ;;
            esac
        done < /tmp/vsenv.txt

        rm -f /tmp/vsenv.txt
        print_success "Visual Studio environment configured"
    else
        print_warning "Visual Studio not found in standard locations"
        print_info "CMake will try to find it automatically"
    fi
fi

# Check CMake
if ! command_exists cmake; then
    print_error "CMake not found. Please install CMake 3.20+"
    exit 1
fi

print_success "CMake found: $(cmake --version | head -1)"

# Check submodules
print_step "Checking submodules..."
git submodule update --init --recursive || {
    print_warning "Submodule update failed (may already be initialized)"
}

# Configure
print_step "Configuring build..."
cmake --preset "$PRESET" || {
    print_error "Configuration failed"
    exit 1
}

print_success "Configuration complete"

# Build
print_step "Building..."
JOBS=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)

cmake --build "build/$PRESET" -j "$JOBS" || {
    print_error "Build failed"
    exit 1
}

print_success "Build complete"

# Verify build output exists
BUILD_DIR="build/$PRESET"

if [ -f "$BUILD_DIR/Engine/libengine.a" ]; then
    print_success "Engine library built: $BUILD_DIR/Engine/libengine.a"
else
    print_error "Engine library not found"
    exit 1
fi

print_complete "Build successful!"
echo ""
print_info "Build directory: $BUILD_DIR"
print_info "Next step: ./scripts/03-install-extension.sh"
echo ""
