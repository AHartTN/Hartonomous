#!/usr/bin/env bash
# ==============================================================================
# Populate Atom Table (Lock Down Geometric Foundation)
# ==============================================================================
# Runs seed_unicode tool to populate ~1.114M Unicode codepoints as Atoms.
#
# Process:
#   1. Reads UCD/UCA metadata from seed database
#   2. Applies Super Fibonacci distribution on S³
#   3. Uses Hopf fibration for geometric projection
#   4. Creates Atom + Physicality records with S³ coordinates
#
# After this completes, Atom table is IMMUTABLE.
# All subsequent intelligence builds on this geometric foundation.
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$SCRIPT_DIR/../lib/common.sh"

SEED_UNICODE="$PROJECT_ROOT/build/linux-release-max-perf/Engine/tools/seed_unicode"

# Environment variables for hartonomous database
export HARTONOMOUS_DB_HOST="${HARTONOMOUS_DB_HOST:-localhost}"
export HARTONOMOUS_DB_PORT="${HARTONOMOUS_DB_PORT:-5432}"
export HARTONOMOUS_DB_USER="${HARTONOMOUS_DB_USER:-postgres}"
export HARTONOMOUS_DB_NAME="hartonomous"

# Environment variables for seed database
export UCD_DB_HOST="${UCD_DB_HOST:-localhost}"
export UCD_DB_PORT="${UCD_DB_PORT:-5432}"
export UCD_DB_USER="${UCD_DB_USER:-postgres}"
export UCD_DB_NAME="ucd_seed"

print_header "Populate Atom Table - Lock Down Geometric Foundation"

# Verify seed_unicode tool exists
if [ ! -f "$SEED_UNICODE" ]; then
    print_error "seed_unicode tool not found: $SEED_UNICODE"
    echo ""
    echo "Build the engine first:"
    echo "  ./scripts/build/build-engine.sh"
    exit 1
fi

# Verify seed database exists
if ! psql -h "$UCD_DB_HOST" -p "$UCD_DB_PORT" -U "$UCD_DB_USER" -lqt | cut -d \| -f 1 | grep -qw "$UCD_DB_NAME"; then
    print_error "Seed database '$UCD_DB_NAME' not found"
    echo ""
    echo "Create seed database and run UCDIngestor first:"
    echo "  1. ./scripts/database/setup-seed-db.sh"
    echo "  2. ./UCDIngestor/setup_db.sh"
    exit 1
fi

# Verify hartonomous database exists
if ! psql -h "$HARTONOMOUS_DB_HOST" -p "$HARTONOMOUS_DB_PORT" -U "$HARTONOMOUS_DB_USER" -lqt | cut -d \| -f 1 | grep -qw "$HARTONOMOUS_DB_NAME"; then
    print_error "Hartonomous database not found"
    echo ""
    echo "Create hartonomous database first:"
    echo "  ./scripts/database/create-hartonomous-db.sh"
    exit 1
fi

print_step "Running seed_unicode tool..."
print_info "This generates ~1.114M Atom records with S³ coordinates"
print_info "Using Super Fibonacci + Hopf fibration distribution"
echo ""

if "$SEED_UNICODE"; then
    print_success "Atom table populated"
    echo ""
    
    # Show statistics
    ATOM_COUNT=$(psql -h "$HARTONOMOUS_DB_HOST" -p "$HARTONOMOUS_DB_PORT" -U "$HARTONOMOUS_DB_USER" -d "$HARTONOMOUS_DB_NAME" -t -c "SELECT COUNT(*) FROM hartonomous.atom;")
    print_info "Total Atoms: $(echo $ATOM_COUNT | tr -d ' ')"
    
    print_complete "Geometric foundation is LOCKED"
    echo ""
    echo "The Atom table is now immutable."
    echo "All intelligence emerges from this geometric structure."
else
    print_error "seed_unicode failed"
    exit 1
fi
