#!/usr/bin/env bash
# Verification script - Check that everything is ready before running full pipeline

set -e

source "$(dirname "$0")/common.sh"

print_header "Hartonomous Setup Verification"

ERRORS=0
WARNINGS=0

# Check 1: Git submodules
print_step "Checking git submodules..."

SUBMODULES=(
    "Engine/external/blake3"
    "Engine/external/eigen"
    "Engine/external/hnswlib"
    "Engine/external/spectra"
    "Engine/external/json"
)

for submod in "${SUBMODULES[@]}"; do
    if [ -d "$submod/.git" ] || [ -f "$submod/.git" ]; then
        print_success "  $submod: OK"
    else
        print_error "  $submod: MISSING"
        ((ERRORS++))
    fi
done

if [ $ERRORS -eq 0 ]; then
    print_success "All submodules present"
else
    print_warning "Run: git submodule update --init --recursive"
fi

echo ""

# Check 2: CMake
print_step "Checking CMake..."

if command_exists cmake; then
    CMAKE_VERSION=$(cmake --version | head -1 | grep -oP '\d+\.\d+' | head -1)
    REQUIRED_VERSION="3.25"

    if [ "$(printf '%s\n' "$REQUIRED_VERSION" "$CMAKE_VERSION" | sort -V | head -n1)" = "$REQUIRED_VERSION" ]; then
        print_success "CMake $CMAKE_VERSION (>= 3.25 required)"
    else
        print_error "CMake $CMAKE_VERSION found, but >= 3.25 required"
        ((ERRORS++))
    fi
else
    print_error "CMake not found"
    ((ERRORS++))
fi

echo ""

# Check 3: C++ Compiler
print_step "Checking C++ compiler..."

if command_exists g++; then
    GCC_VERSION=$(g++ --version | head -1)
    print_success "GCC: $GCC_VERSION"
elif command_exists clang++; then
    CLANG_VERSION=$(clang++ --version | head -1)
    print_success "Clang: $CLANG_VERSION"
elif command_exists cl; then
    MSVC_VERSION=$(cl 2>&1 | head -1)
    print_success "MSVC: $MSVC_VERSION"
else
    print_error "No C++ compiler found (need GCC 11+, Clang 14+, or MSVC 2022+)"
    ((ERRORS++))
fi

echo ""

# Check 4: PostgreSQL
print_step "Checking PostgreSQL..."

if command_exists psql; then
    PSQL_VERSION=$(psql --version)
    print_success "psql: $PSQL_VERSION"
else
    print_error "psql not found"
    ((ERRORS++))
fi

if command_exists pg_config; then
    PG_VERSION=$(pg_config --version)
    print_success "pg_config: $PG_VERSION"
else
    print_error "pg_config not found (needed for extension)"
    ((ERRORS++))
fi

echo ""

# Check 5: PostgreSQL Connection
print_step "Checking PostgreSQL connection..."

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}

if psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -c "SELECT 1" &> /dev/null; then
    print_success "Connected to PostgreSQL at $PGHOST:$PGPORT"

    # Check PostGIS
    if psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -c "CREATE EXTENSION IF NOT EXISTS postgis; SELECT PostGIS_Version();" &> /dev/null; then
        print_success "PostGIS extension available"
    else
        print_warning "PostGIS extension not available (will try to install during setup)"
        ((WARNINGS++))
    fi
else
    print_error "Cannot connect to PostgreSQL at $PGHOST:$PGPORT"
    print_info "Check that PostgreSQL is running and credentials are correct"
    print_info "Set PGHOST, PGPORT, PGUSER, PGPASSWORD as needed"
    ((ERRORS++))
fi

echo ""

# Check 6: Test data
print_step "Checking test data..."

REPO_ROOT=$(get_repo_root)

if [ -f "$REPO_ROOT/test-data/moby-dick.txt" ]; then
    SIZE=$(du -h "$REPO_ROOT/test-data/moby-dick.txt" | cut -f1)
    print_success "Moby Dick: $SIZE"
else
    print_warning "Moby Dick not found: test-data/moby-dick.txt"
    print_info "Download from: https://www.gutenberg.org/files/2701/2701-0.txt"
    ((WARNINGS++))
fi

if [ -d "$REPO_ROOT/test-data/minilm" ]; then
    print_success "minilm model: OK"
else
    print_warning "minilm model not found: test-data/minilm/"
    print_info "Download from HuggingFace: sentence-transformers/all-MiniLM-L6-v2"
    ((WARNINGS++))
fi

echo ""

# Check 7: Scripts executable
print_step "Checking script permissions..."

SCRIPTS=(
    "scripts/00-full-pipeline.sh"
    "scripts/01-rebuild-database.sh"
    "scripts/02-build-all.sh"
    "scripts/03-install-extension.sh"
    "scripts/04-ingest-test-data.sh"
    "scripts/05-run-queries.sh"
)

for script in "${SCRIPTS[@]}"; do
    if [ -x "$script" ]; then
        print_success "  $script: executable"
    else
        print_warning "  $script: not executable"
        chmod +x "$script" 2>/dev/null && print_info "    Fixed: chmod +x $script" || print_warning "    Run: chmod +x scripts/*.sh"
    fi
done

echo ""

# Check 8: Schema files
print_step "Checking schema files..."

if [ -f "$REPO_ROOT/schema/hartonomous_schema.sql" ]; then
    print_success "hartonomous_schema.sql: OK"
else
    print_error "Schema file not found: schema/hartonomous_schema.sql"
    ((ERRORS++))
fi

echo ""

# Summary
print_header "Verification Summary"

if [ $ERRORS -eq 0 ] && [ $WARNINGS -eq 0 ]; then
    print_complete "All checks passed! Ready to run full pipeline."
    echo ""
    print_info "Next step: ./scripts/00-full-pipeline.sh"
elif [ $ERRORS -eq 0 ]; then
    print_warning "Ready to run, but $WARNINGS warnings found"
    echo ""
    print_info "You can proceed, but some features may not work without test data"
    print_info "Next step: ./scripts/00-full-pipeline.sh"
else
    print_error "Setup incomplete: $ERRORS errors, $WARNINGS warnings"
    echo ""
    print_info "Fix the errors above before running the full pipeline"
    exit 1
fi

echo ""
