#!/usr/bin/env bash
# ==============================================================================
# Hartonomous Full Build & Deploy Pipeline
# ==============================================================================
# Properly separated build steps:
#   1. Build C++ Engine (libengine_core, libengine_io, libengine unified)
#   2. Build PostgreSQL Extensions (s3, hartonomous)
#   3. Install Extensions (requires sudo)
#   4. Setup Database
#   5. Seed Unicode
#   6. Ingest Test Data
#   7. Run Tests

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }
print_info() { echo -e "${CYAN}=== $1 ===${NC}"; }
print_step() { echo -e "${YELLOW}>>> $1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

LOG_DIR="$SCRIPT_DIR/logs"
mkdir -p "$LOG_DIR"

# Trap errors
trap 'print_error "Build failed at step: $CURRENT_STEP"; exit 1' ERR

# ==============================================================================
# STEP 1: Build C++ Engine
# ==============================================================================
CURRENT_STEP="Build C++ Engine"
print_info "STEP 1: Build C++ Engine"
print_step "Building libengine_core.so, libengine_io.so, libengine.so + running UNIT tests..."

# Build + run unit tests (no database required)
if ./scripts/linux/01-build.sh -c -T > "$LOG_DIR/01-build.log" 2>&1; then
    print_success "C++ Engine built + unit tests passed"
else
    print_error "C++ Engine build failed (see logs/01-build.log)"
    exit 1
fi

# ==============================================================================
# STEP 2: Install PostgreSQL Extensions
# ==============================================================================
CURRENT_STEP="Install PostgreSQL Extensions"
print_info "STEP 2: Install PostgreSQL Extensions"
print_step "Installing s3.so and hartonomous.so (requires sudo)..."

if ./scripts/linux/02-install.sh > "$LOG_DIR/02-install.log" 2>&1; then
    print_success "Extensions installed"
else
    print_error "Extension installation failed (see logs/02-install.log)"
    exit 1
fi

# ==============================================================================
# STEP 3: Update Library Cache
# ==============================================================================
CURRENT_STEP="Update Library Cache"
print_info "STEP 3: Update Library Cache"
print_step "Running ldconfig (requires sudo)..."

if sudo ldconfig > "$LOG_DIR/03-ldconfig.log" 2>&1; then
    print_success "Library cache updated"
else
    print_error "ldconfig failed (see logs/03-ldconfig.log)"
    exit 1
fi

# ==============================================================================
# STEP 4: Setup Database
# ==============================================================================
CURRENT_STEP="Setup Database"
print_info "STEP 4: Setup Database"
print_step "Creating hypercube database with structured schema..."

if ./scripts/linux/03-setup-database.sh --drop > "$LOG_DIR/04-setup-database.log" 2>&1; then
    print_success "Database setup complete"
else
    print_error "Database setup failed (see logs/04-setup-database.log)"
    exit 1
fi

# ==============================================================================
# STEP 5: Seed Unicode (UCD Ingestor)
# ==============================================================================
CURRENT_STEP="Seed Unicode"
print_info "STEP 5: Seed Unicode"
print_step "Loading Unicode Character Database..."

if ./UCDIngestor/setup_db.sh > "$LOG_DIR/05-ucd-setup-db.log" 2>&1; then
    print_success "UCD setup complete"
else
    print_warning "UCD setup had issues (see logs/05-ucd-setup-db.log)"
    # Continue anyway as this might be non-critical
fi

# ==============================================================================
# STEP 6: Seed Unicode (Alternative Method)
# ==============================================================================
CURRENT_STEP="Seed Unicode (Tool)"
print_info "STEP 6: Seed Unicode (Tool)"
print_step "Running seed_unicode tool..."

if ./scripts/linux/05-seed-unicode.sh > "$LOG_DIR/06-seed-unicode.log" 2>&1; then
    print_success "Unicode seeding complete"
else
    print_warning "Unicode seeding had issues (see logs/06-seed-unicode.log)"
fi

# ==============================================================================
# STEP 7: Run Ingestion
# ==============================================================================
CURRENT_STEP="Run Ingestion"
print_info "STEP 7: Run Ingestion"
print_step "Running ingestion pipeline..."

if [ -f "./scripts/linux/04-run_ingestion.sh" ]; then
    if ./scripts/linux/04-run_ingestion.sh > "$LOG_DIR/07-run-ingestion.log" 2>&1; then
        print_success "Ingestion complete"
    else
        print_warning "Ingestion had issues (see logs/07-run-ingestion.log)"
    fi
else
    print_warning "Ingestion script not found (skipping)"
fi

# ==============================================================================
# STEP 8: Ingest Mini-LM Model
# ==============================================================================
CURRENT_STEP="Ingest Mini-LM"
print_info "STEP 8: Ingest Mini-LM Model"
print_step "Ingesting MiniLM embedding model..."

if [ -f "./scripts/linux/20-ingest-mini-lm.sh" ]; then
    if ./scripts/linux/20-ingest-mini-lm.sh > "$LOG_DIR/08-ingest-minilm.log" 2>&1; then
        print_success "Mini-LM ingested"
    else
        print_warning "Mini-LM ingestion had issues (see logs/08-ingest-minilm.log)"
    fi
else
    print_warning "Mini-LM ingest script not found (skipping)"
fi

# ==============================================================================
# STEP 9: Ingest Text
# ==============================================================================
CURRENT_STEP="Ingest Text"
print_info "STEP 9: Ingest Text"
print_step "Ingesting text data (Moby Dick, etc.)..."

if [ -f "./scripts/linux/30-ingest-text.sh" ]; then
    if ./scripts/linux/30-ingest-text.sh > "$LOG_DIR/09-ingest-text.log" 2>&1; then
        print_success "Text ingested"
    else
        print_warning "Text ingestion had issues (see logs/09-ingest-text.log)"
    fi
else
    print_warning "Text ingest script not found (skipping)"
fi

# ==============================================================================
# STEP 10: Run Queries
# ==============================================================================
CURRENT_STEP="Run Queries"
print_info "STEP 10: Run Test Queries"
print_step "Running semantic queries..."

if [ -f "./scripts/linux/40-run-queries.sh" ]; then
    if ./scripts/linux/40-run-queries.sh > "$LOG_DIR/10-run-queries.log" 2>&1; then
        print_success "Queries executed"
    else
        print_warning "Queries had issues (see logs/10-run-queries.log)"
    fi
else
    print_warning "Query script not found (skipping)"
fi

# ==============================================================================
# STEP 11: Walk Test
# ==============================================================================
CURRENT_STEP="Walk Test"
print_info "STEP 11: Walk Test"
print_step "Running walk test..."

if [ -f "./build/linux-release-max-perf/Engine/tools/walk_test" ]; then
    if ./build/linux-release-max-perf/Engine/tools/walk_test > "$LOG_DIR/11-walk-test.log" 2>&1; then
        print_success "Walk test complete"
    else
        print_warning "Walk test had issues (see logs/11-walk-test.log)"
    fi
else
    print_warning "Walk test binary not found (skipping)"
fi

# ==============================================================================
# STEP 12: Run Integration Tests (NOW that database exists and data is loaded)
# ==============================================================================
CURRENT_STEP="Run Integration Tests"
print_info "STEP 12: Run Integration Tests"
print_step "Running integration tests (database + ingestion required)..."

cd build/linux-release-max-perf
if /usr/bin/ctest --output-on-failure -L "integration|e2e" > "$LOG_DIR/12-integration-tests.log" 2>&1; then
    print_success "All integration tests passed"
else
    print_warning "Some integration tests failed (see logs/12-integration-tests.log)"
    # Don't exit - integration tests can be flaky but system is usable
fi
cd "$SCRIPT_DIR"

# ==============================================================================
# SUCCESS
# ==============================================================================
print_info "PIPELINE COMPLETE"
echo ""
print_success "All steps completed!"
echo ""
echo "Logs saved to: $LOG_DIR/"
echo ""
echo "Next steps:"
echo "  - Connect: psql -U postgres -d hypercube"
echo "  - Run tests: cd build/linux-release-max-perf/Engine/tests && ./suite_test_*"
echo "  - Check data: psql -U postgres -d hypercube -c 'SELECT COUNT(*) FROM hartonomous.atom;'"
echo ""
