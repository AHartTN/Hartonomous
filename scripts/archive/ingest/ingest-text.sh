#!/usr/bin/env bash
# ==============================================================================
# Ingest Text (Build Composition and Relation Layers)
# ==============================================================================
# Ingests text to build Composition (n-grams of Atoms) and Relation (n-grams of Compositions) layers.
#
# Process:
#   1. Parse text into Unicode codepoints
#   2. Map to Atoms (geometric coordinates on S³)
#   3. Create Compositions (n-grams: words, phrases)
#   4. Record CompositionSequence (trajectory through S³)
#   5. Create Relations (co-occurrence patterns)
#   6. Record RelationSequence (higher-order trajectories)
#   7. Update RelationRating (ELO scores)
#   8. Record RelationEvidence (source tracking)
#
# Intelligence emerges from navigating these geometric relationships.
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$SCRIPT_DIR/../lib/common.sh"

INGEST_TEXT="$PROJECT_ROOT/build/linux-release-max-perf/Engine/tools/ingest_text"

TEXT_FILE="${1:-$PROJECT_ROOT/test-data/moby_dick.txt}"

# Environment for hartonomous database
export HARTONOMOUS_DB_HOST="${HARTONOMOUS_DB_HOST:-localhost}"
export HARTONOMOUS_DB_PORT="${HARTONOMOUS_DB_PORT:-5432}"
export HARTONOMOUS_DB_USER="${HARTONOMOUS_DB_USER:-postgres}"
export HARTONOMOUS_DB_NAME="hartonomous"

# Ensure we use locally built libraries
export LD_LIBRARY_PATH="$PROJECT_ROOT/build/linux-release-max-perf/Engine:$LD_LIBRARY_PATH"

print_header "Ingest Text - Build Composition & Relation Layers"

# Verify tool
if [ ! -f "$INGEST_TEXT" ]; then
    print_error "ingest_text tool not found: $INGEST_TEXT"
    echo "Build the engine first: ./scripts/build/build-engine.sh"
    exit 1
fi

# Verify text file
if [ ! -f "$TEXT_FILE" ]; then
    print_error "Text file not found: $TEXT_FILE"
    echo ""
    echo "Usage: $0 [text_file]"
    echo "Example: $0 test-data/moby_dick.txt"
    exit 1
fi

print_step "Ingesting text from: $TEXT_FILE"
print_info "Creating Compositions (n-grams of Atoms)"
print_info "Creating Relations (n-grams of Compositions)"
print_info "Recording trajectories through S³"
echo ""

if "$INGEST_TEXT" file "$TEXT_FILE"; then
    print_success "Text ingested successfully"
    
    # Show statistics
    echo ""
    print_info "Database statistics:"
    psql -h "$HARTONOMOUS_DB_HOST" -p "$HARTONOMOUS_DB_PORT" -U "$HARTONOMOUS_DB_USER" -d "$HARTONOMOUS_DB_NAME" -c "
        SELECT 
            'Atoms' as layer, COUNT(*) as count FROM hartonomous.atom
        UNION ALL
        SELECT 'Compositions', COUNT(*) FROM hartonomous.composition
        UNION ALL
        SELECT 'Relations', COUNT(*) FROM hartonomous.relation;
    "
    
    print_complete "Geometric intelligence layers constructed"
else
    print_error "Text ingestion failed"
    exit 1
fi
