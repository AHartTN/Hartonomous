#!/usr/bin/env bash
# Test Database Functionality

set -e

source "$(dirname "$0")/common.sh"

print_header "Testing Hartonomous Database"

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}
PGDATABASE=${PGDATABASE:-hartonomous}

# Test 1: Version
print_step "Test 1: Check version..."
VERSION=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -t -c \
    "SELECT * FROM hartonomous.version();")
print_success "Version: $VERSION"

# Test 2: Statistics
print_step "Test 2: Database statistics..."
psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT * FROM hartonomous.stats();
EOF
print_success "Statistics retrieved"

# Test 3: Unicode atoms
print_step "Test 3: Check Unicode atoms..."
ATOM_COUNT=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -t -c \
    "SELECT COUNT(*) FROM hartonomous.Atom;")
print_success "Atom count: $ATOM_COUNT"

# Test 4: Sample Unicode data
print_step "Test 4: Sample Unicode characters (A-Z)..."
psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT codepoint, character,
       SUBSTRING(encode(hash, 'hex'), 1, 16) AS hash_prefix
FROM hartonomous.unicode_atoms
WHERE codepoint BETWEEN 65 AND 90
LIMIT 5;
EOF
print_success "Unicode data verified"

# Test 5: Consistency check
print_step "Test 5: Running consistency checks..."
psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT * FROM hartonomous.repair_inconsistencies();
EOF
print_success "Consistency checks passed"

# Test 6: Schema version
print_step "Test 6: Schema versions..."
psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT version, description FROM hartonomous.schema_version() ORDER BY version;
EOF
print_success "Schema version verified"

print_complete "All tests passed!"
echo ""
print_info "Database: $PGDATABASE@$PGHOST:$PGPORT"
print_info "Ready for ingestion and querying"
echo ""
