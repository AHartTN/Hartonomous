#!/usr/bin/env bash
# Script 1: Rebuild Database (Drop and Recreate hypercube)

set -e

source "$(dirname "$0")/common.sh"

print_header "Rebuilding Database: hypercube"

# Get database connection info from environment or use defaults
PGDATABASE=${PGDATABASE:-hypercube}

print_info "Database: $PGDATABASE"
print_info "Using peer authentication (no password required)"

# Drop existing database
print_step "Dropping existing database..."
psql -U postgres -d postgres -c "DROP DATABASE IF EXISTS $PGDATABASE;" 2>/dev/null || true
print_success "Database dropped"

# Create new database
print_step "Creating new database..."
psql -U postgres -d postgres -c "CREATE DATABASE $PGDATABASE;"
print_success "Database created"

# Install PostGIS extension
print_step "Installing PostGIS extension..."
psql -U postgres -d "$PGDATABASE" -c "CREATE EXTENSION IF NOT EXISTS postgis;"
print_success "PostGIS installed"

# Apply schema
print_step "Applying schema..."

SCHEMA_DIR="$(dirname "$0")/../PostgresExtension/schema"

for schema_file in \
    "$SCHEMA_DIR/hartonomous_schema.sql" \
    "$SCHEMA_DIR/relations_schema.sql" \
    "$SCHEMA_DIR/postgis_spatial_functions.sql" \
    "$SCHEMA_DIR/security_model.sql"
do
    if [ -f "$schema_file" ]; then
        print_info "  Applying $(basename "$schema_file")..."
        psql -U postgres -d "$PGDATABASE" -f "$schema_file" || {
            print_error "Failed to apply $(basename "$schema_file")"
            exit 1
        }
    else
        print_warning "  Schema file not found: $(basename "$schema_file")"
    fi
done

print_success "Schema applied"

# Create indexes
print_step "Creating indexes..."

psql -U postgres -d "$PGDATABASE" << 'EOF'
-- Spatial indexes
CREATE INDEX IF NOT EXISTS idx_atoms_s3_position
    ON atoms USING GIST (st_makepoint(s3_x, s3_y, s3_z));

CREATE INDEX IF NOT EXISTS idx_compositions_centroid
    ON compositions USING GIST (st_makepoint(centroid_x, centroid_y, centroid_z));

-- Hilbert indexes
CREATE INDEX IF NOT EXISTS idx_atoms_hilbert
    ON atoms USING BTREE (hilbert_index);

CREATE INDEX IF NOT EXISTS idx_compositions_hilbert
    ON compositions USING BTREE (hilbert_index);

-- Hash indexes
CREATE INDEX IF NOT EXISTS idx_atoms_hash
    ON atoms USING BTREE (hash);

CREATE INDEX IF NOT EXISTS idx_compositions_hash
    ON compositions USING BTREE (hash);

CREATE INDEX IF NOT EXISTS idx_relations_hash
    ON relations USING BTREE (hash);

-- Text search indexes
CREATE INDEX IF NOT EXISTS idx_compositions_text
    ON compositions USING BTREE (LOWER(text));
EOF

print_success "Indexes created"

# Verify
print_step "Verifying database..."
TABLE_COUNT=$(psql -U postgres -d "$PGDATABASE" -t -c \
    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';")

print_success "Verification complete: $TABLE_COUNT tables created"

print_complete "Database rebuild complete!"
echo ""
print_info "Connection string:"
print_info "  psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE"
echo ""
