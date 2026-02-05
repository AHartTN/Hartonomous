#!/usr/bin/env bash
# Quick Test Build Script (No Sudo Required)
# Tests all the stability fixes

set -e

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

echo "======================================"
echo "Hartonomous Stability Test Build"
echo "======================================"
echo ""

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'

# Step 1: Clean build
echo -e "${CYAN}Step 1: Cleaning previous build...${NC}"
rm -rf build/linux-release-max-perf
echo -e "${GREEN}✓ Clean complete${NC}"
echo ""

# Step 2: Configure
echo -e "${CYAN}Step 2: Configuring CMake...${NC}"
cmake --preset linux-release-max-perf
echo -e "${GREEN}✓ Configuration complete${NC}"
echo ""

# Step 3: Build C++
echo -e "${CYAN}Step 3: Building C++ engine...${NC}"
cmake --build build/linux-release-max-perf -j$(nproc)
echo -e "${GREEN}✓ C++ build complete${NC}"
echo ""

# Step 4: Verify libraries
echo -e "${CYAN}Step 4: Verifying libraries...${NC}"
LIBS_OK=true

if [ ! -f "build/linux-release-max-perf/Engine/libengine_core.so" ]; then
    echo -e "${RED}✗ libengine_core.so not found${NC}"
    LIBS_OK=false
fi

if [ ! -f "build/linux-release-max-perf/Engine/libengine_io.so" ]; then
    echo -e "${RED}✗ libengine_io.so not found${NC}"
    LIBS_OK=false
fi

if [ ! -f "build/linux-release-max-perf/Engine/libengine.so" ]; then
    echo -e "${RED}✗ libengine.so not found (THIS IS THE KEY FIX!)${NC}"
    LIBS_OK=false
else
    echo -e "${GREEN}✓ libengine.so found (NEW - for .NET interop)${NC}"
fi

if [ "$LIBS_OK" = true ]; then
    echo -e "${GREEN}✓ All libraries present${NC}"
    ls -lh build/linux-release-max-perf/Engine/*.so
else
    echo -e "${RED}✗ Some libraries missing${NC}"
    exit 1
fi
echo ""

# Step 5: Build .NET
echo -e "${CYAN}Step 5: Building .NET solution...${NC}"
cd app-layer
dotnet clean > /dev/null 2>&1
dotnet restore > /dev/null 2>&1
if dotnet build -c Release; then
    echo -e "${GREEN}✓ .NET build complete${NC}"
else
    echo -e "${RED}✗ .NET build failed${NC}"
    exit 1
fi
cd ..
echo ""

# Step 6: Run basic C++ tests
echo -e "${CYAN}Step 6: Running C++ tests...${NC}"
cd build/linux-release-max-perf/Engine/tests

TEST_PASSED=0
TEST_FAILED=0

for test_exe in suite_test_*; do
    if [ -x "$test_exe" ]; then
        echo -n "  Running $test_exe... "
        if ./"$test_exe" > /dev/null 2>&1; then
            echo -e "${GREEN}PASS${NC}"
            TEST_PASSED=$((TEST_PASSED + 1))
        else
            echo -e "${RED}FAIL${NC}"
            TEST_FAILED=$((TEST_FAILED + 1))
        fi
    fi
done

cd "$PROJECT_ROOT"

echo ""
echo "Test Results: ${GREEN}$TEST_PASSED passed${NC}, ${RED}$TEST_FAILED failed${NC}"
echo ""

# Summary
echo "======================================"
echo -e "${GREEN}Build Test Complete!${NC}"
echo "======================================"
echo ""
echo "Next steps:"
echo "  1. Install PostgreSQL extensions (requires sudo):"
echo "     ./scripts/linux/02-install.sh"
echo ""
echo "  2. Setup database:"
echo "     ./scripts/linux/03-setup-database.sh --drop"
echo ""
echo "  3. Ingest test data:"
echo "     ./scripts/linux/04-run_ingestion.sh"
echo ""
echo "See STABILITY_FIXES.md for detailed testing plan"
echo ""
