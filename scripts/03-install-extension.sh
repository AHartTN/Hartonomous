#!/usr/bin/env bash
# Script 3: Install PostgreSQL Extension

set -e

source "$(dirname "$0")/common.sh"

print_header "Installing PostgreSQL Extension"

REPO_ROOT="$(get_repo_root)"
cd "$REPO_ROOT"

PRESET=${1:-release-native}
BUILD_DIR="build/$PRESET"

# Check if PostgreSQL is installed
if ! command_exists pg_config; then
    print_error "pg_config not found. Please install PostgreSQL development files."
    print_info "Ubuntu/Debian: sudo apt install postgresql-server-dev-15"
    print_info "macOS: brew install postgresql"
    exit 1
fi

PG_VERSION=$(pg_config --version | grep -oP '\d+' | head -1)
print_success "PostgreSQL found: version $PG_VERSION"

# Get PostgreSQL directories
PKGLIBDIR=$(pg_config --pkglibdir)
SHAREDIR=$(pg_config --sharedir)

print_info "Extension directory: $PKGLIBDIR"
print_info "Share directory: $SHAREDIR"

# Check if build exists
if [ ! -d "$BUILD_DIR" ]; then
    print_error "Build directory not found: $BUILD_DIR"
    print_info "Run ./scripts/02-build-all.sh first"
    exit 1
fi

# Create simple PostgreSQL extension (for now, just install SQL functions)
print_step "Installing SQL functions..."

EXT_DIR="$SHAREDIR/extension"
sudo mkdir -p "$EXT_DIR"

# Create hartonomous.control
print_info "Creating hartonomous.control..."
sudo tee "$EXT_DIR/hartonomous.control" > /dev/null << EOF
# Hartonomous extension
comment = 'Hartonomous - Universal substrate for intelligence'
default_version = '0.1.0'
module_pathname = '\$libdir/hartonomous'
relocatable = true
requires = 'postgis'
EOF

# Create hartonomous--0.1.0.sql
print_info "Creating hartonomous--0.1.0.sql..."
sudo tee "$EXT_DIR/hartonomous--0.1.0.sql" > /dev/null << 'EOF'
-- Hartonomous Extension SQL

-- Version function
CREATE OR REPLACE FUNCTION hartonomous_version()
RETURNS TEXT
LANGUAGE SQL
IMMUTABLE
AS $$
    SELECT '0.1.0'::TEXT;
$$;

-- Helper: Ingest text (placeholder - actual implementation in C++)
CREATE OR REPLACE FUNCTION ingest_text_placeholder(input_text TEXT)
RETURNS JSONB
LANGUAGE PLPGSQL
AS $$
DECLARE
    result JSONB;
BEGIN
    -- Placeholder: This would call C++ implementation
    -- For now, return mock stats
    result := jsonb_build_object(
        'atoms_new', 0,
        'compositions_new', 0,
        'relations_new', 0,
        'status', 'Use C++ ingester for now'
    );
    RETURN result;
END;
$$;

-- Helper: Semantic query (placeholder)
CREATE OR REPLACE FUNCTION semantic_query_placeholder(query_text TEXT)
RETURNS TABLE(result_text TEXT, confidence DOUBLE PRECISION)
LANGUAGE SQL
AS $$
    SELECT
        c.text,
        1.0 AS confidence
    FROM compositions c
    WHERE c.text ILIKE '%' || query_text || '%'
    LIMIT 10;
$$;

EOF

print_success "Extension files created"

# Test installation
print_step "Testing extension installation..."

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}
PGDATABASE=${PGDATABASE:-hypercube}

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF' || {
    print_error "Extension installation test failed"
    exit 1
}
-- Drop if exists
DROP EXTENSION IF EXISTS hartonomous CASCADE;

-- Create extension
CREATE EXTENSION hartonomous;

-- Test version function
SELECT hartonomous_version();
EOF

print_success "Extension installed and tested"

print_complete "Extension installation complete!"
echo ""
print_info "Extension installed in: $EXT_DIR"
print_info "Test in PostgreSQL:"
print_info "  CREATE EXTENSION hartonomous;"
print_info "  SELECT hartonomous_version();"
echo ""
