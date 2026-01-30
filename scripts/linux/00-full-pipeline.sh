#!/usr/bin/env bash
# Script 0: Full Pipeline - Run everything from start to finish
# This orchestrates the complete setup, build, and test process

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/common.sh"

print_header "Hartonomous Full Pipeline"
print_info "This will:"
print_info "  1. Rebuild the database"
print_info "  2. Build all C++ code"
print_info "  3. Install PostgreSQL extension"
print_info "  4. Ingest test data (Moby Dick + minilm)"
print_info "  5. Run semantic queries"
echo ""

# Parse arguments
PRESET=${1:-release-native}
SKIP_DB=${SKIP_DB:-0}
SKIP_BUILD=${SKIP_BUILD:-0}
SKIP_EXTENSION=${SKIP_EXTENSION:-0}
SKIP_INGEST=${SKIP_INGEST:-0}
SKIP_QUERIES=${SKIP_QUERIES:-0}

print_info "Build preset: $PRESET"
echo ""

# Step 1: Rebuild database
if [ "$SKIP_DB" -eq 0 ]; then
    print_step "Step 1/5: Rebuilding database..."
    if ! "$SCRIPT_DIR/01-rebuild-database.sh"; then
        print_error "Database rebuild failed"
        exit 1
    fi
    print_success "Database ready"
else
    print_warning "Skipping database rebuild (SKIP_DB=1)"
fi

echo ""

# Step 2: Build all
if [ "$SKIP_BUILD" -eq 0 ]; then
    print_step "Step 2/5: Building C++ code..."
    if ! "$SCRIPT_DIR/02-build-all.sh" Release "$PRESET"; then
        print_error "Build failed"
        exit 1
    fi
    print_success "Build complete"
else
    print_warning "Skipping build (SKIP_BUILD=1)"
fi

echo ""

# Step 3: Install extension
if [ "$SKIP_EXTENSION" -eq 0 ]; then
    print_step "Step 3/5: Installing PostgreSQL extension..."
    if ! "$SCRIPT_DIR/03-install-extension.sh" "$PRESET"; then
        print_error "Extension installation failed"
        exit 1
    fi
    print_success "Extension installed"
else
    print_warning "Skipping extension install (SKIP_EXTENSION=1)"
fi

echo ""

# Step 4: Ingest test data
if [ "$SKIP_INGEST" -eq 0 ]; then
    print_step "Step 4/5: Ingesting test data..."
    if ! "$SCRIPT_DIR/04-ingest-test-data.sh" "$PRESET"; then
        print_error "Data ingestion failed"
        exit 1
    fi
    print_success "Test data ingested"
else
    print_warning "Skipping data ingestion (SKIP_INGEST=1)"
fi

echo ""

# Step 5: Run queries
if [ "$SKIP_QUERIES" -eq 0 ]; then
    print_step "Step 5/5: Running semantic queries..."
    if ! "$SCRIPT_DIR/05-run-queries.sh"; then
        print_error "Queries failed"
        exit 1
    fi
    print_success "Queries complete"
else
    print_warning "Skipping queries (SKIP_QUERIES=1)"
fi

echo ""

print_complete "Full Pipeline Complete!"
echo ""
print_info "Summary:"
print_info "  ✓ Database: hypercube (PostgreSQL)"
print_info "  ✓ Extension: hartonomous v0.1.0"
print_info "  ✓ Test data: Moby Dick + minilm"
print_info "  ✓ Queries: Semantic search working"
echo ""
print_info "Next steps:"
print_info "  - Connect to database: psql -d hypercube"
print_info "  - Run custom queries: psql -d hypercube -c \"SELECT ...\""
print_info "  - Check stats: psql -d hypercube -c \"SELECT COUNT(*) FROM compositions;\""
echo ""
print_info "Environment variables used:"
print_info "  PGHOST=${PGHOST:-localhost}"
print_info "  PGPORT=${PGPORT:-5432}"
print_info "  PGDATABASE=${PGDATABASE:-hypercube}"
print_info "  PGUSER=${PGUSER:-postgres}"
echo ""
print_info "Skip steps with environment variables:"
print_info "  SKIP_DB=1 SKIP_BUILD=1 ./scripts/00-full-pipeline.sh"
echo ""
