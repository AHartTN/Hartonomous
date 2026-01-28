#!/usr/bin/env bash
# Script 4: Ingest Test Data

set -e

source "$(dirname "$0")/common.sh"

print_header "Ingesting Test Data"

REPO_ROOT="$(get_repo_root)"
cd "$REPO_ROOT"

PRESET=${1:-linux-release-max-perf}
BUILD_DIR="build/$PRESET"

if [ ! -d "$BUILD_DIR" ]; then
    print_error "Build directory not found: $BUILD_DIR"
    exit 1
fi

PGDATABASE=${PGDATABASE:-hypercube}

# Step 1: Seed Unicode atoms
print_step "Seeding Unicode codepoints..."
if [ -f "$BUILD_DIR/Engine/tools/seed_unicode" ]; then
    PGDATABASE=$PGDATABASE "$BUILD_DIR/Engine/tools/seed_unicode" || {
        print_error "Unicode seeding failed"
        exit 1
    }
    print_success "Unicode atoms seeded"
else
    print_error "seed_unicode tool not found. Run ./scripts/02-build-all.sh first"
    exit 1
fi

# Step 2: Ingest sample text
print_step "Ingesting sample compositions..."
psql -U postgres -d "$PGDATABASE" << 'EOF'
INSERT INTO compositions (hash, text, centroid_x, centroid_y, centroid_z, centroid_w, hilbert_index)
VALUES
    (decode('1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef', 'hex'), 'Call', 0.5, 0.5, 0.5, 0.5, 1),
    (decode('2234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef', 'hex'), 'me', 0.5, 0.5, 0.5, 0.5, 2),
    (decode('3234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef', 'hex'), 'Ishmael', 0.5, 0.5, 0.5, 0.5, 3),
    (decode('4234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef', 'hex'), 'whale', 0.5, 0.5, 0.5, 0.5, 4),
    (decode('5234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef', 'hex'), 'Moby', 0.5, 0.5, 0.5, 0.5, 5),
    (decode('6234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef', 'hex'), 'Dick', 0.5, 0.5, 0.5, 0.5, 6)
ON CONFLICT (hash) DO NOTHING;
EOF
print_success "Sample data ingested"

# Step 3: Verify
print_step "Verifying..."
ATOM_COUNT=$(psql -U postgres -d "$PGDATABASE" -t -c "SELECT COUNT(*) FROM atoms;")
COMP_COUNT=$(psql -U postgres -d "$PGDATABASE" -t -c "SELECT COUNT(*) FROM compositions;")

print_success "Counts: Atoms=$ATOM_COUNT Compositions=$COMP_COUNT"
print_complete "Ingestion complete!"
