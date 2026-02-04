#!/usr/bin/env bash
# Script 1: Setup/Repair Database (IDEMPOTENT)
# Safe to run multiple times - will repair and update as needed

set -e

source "$(dirname "$0")/common.sh"

print_header "Database Setup & Repair"

REPO_ROOT="$(get_repo_root)"
cd "$REPO_ROOT"

# Check PostgreSQL
if ! check_postgres; then
    exit 1
fi

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}
PGDATABASE=${PGDATABASE:-hypercube}

# Check if destructive rebuild requested
REBUILD=${REBUILD:-0}

if [ "$REBUILD" -eq 1 ]; then
    print_warning "REBUILD=1: This will DROP the existing database: $PGDATABASE"
    read -p "Continue? (y/N) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_info "Aborted"
        exit 0
    fi

    # Drop database
    print_step "Dropping database: $PGDATABASE..."
    psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres << EOF
DROP DATABASE IF EXISTS $PGDATABASE;
EOF

    print_success "Database dropped"
fi

# Create database if not exists (idempotent)
print_step "Creating database if not exists: $PGDATABASE..."

DB_EXISTS=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -t -c \
    "SELECT 1 FROM pg_database WHERE datname = '$PGDATABASE';" | xargs || echo "0")

if [ "$DB_EXISTS" != "1" ]; then
    psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres << EOF
CREATE DATABASE $PGDATABASE;
EOF
    print_success "Database created"
else
    print_success "Database already exists"
fi

# Apply schema files (idempotent)
SCHEMA_DIR="$REPO_ROOT/schema"

print_step "Applying schema files..."

for schema_file in "$SCHEMA_DIR"/*.sql; do
    if [ -f "$schema_file" ]; then
        print_info "  Applying: $(basename "$schema_file")"

        psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -f "$schema_file" -q || {
            print_error "Failed to apply: $(basename "$schema_file")"
            exit 1
        }
    fi
done

print_success "All schema files applied"

# Run repair function
print_step "Running consistency checks..."

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF' | grep -v "^$" || true
SELECT * FROM hartonomous.repair_inconsistencies();
EOF

print_success "Consistency checks complete"

# Show statistics
print_step "Database statistics..."

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
\pset border 2
SELECT * FROM hartonomous.stats();

SELECT 'Schema Version'::TEXT AS info, MAX(version)::TEXT AS value
FROM hartonomous_internal.schema_version
UNION ALL
SELECT 'Latest Update'::TEXT, MAX(applied_at)::TEXT
FROM hartonomous_internal.schema_version;
EOF

print_complete "Database setup complete!"
echo ""
print_info "Database: $PGDATABASE@$PGHOST:$PGPORT"
echo ""
print_info "Commands:"
print_info "  Connect:  psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE"
print_info "  Rebuild:  REBUILD=1 ./scripts/01-setup-database.sh"
print_info "  Repair:   ./scripts/01-setup-database.sh"
echo ""
print_info "Next step: ./scripts/02-build-all.sh"
echo ""
