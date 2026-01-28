#!/usr/bin/env bash
# Script 3: Install PostgreSQL Extension

set -e

source "$(dirname "$0")/common.sh"

print_header "Installing PostgreSQL Extension"

REPO_ROOT="$(get_repo_root)"
cd "$REPO_ROOT"

PRESET=${1:-linux-release-max-perf}
BUILD_DIR="build/$PRESET"

# Check if running as root/sudo
if [ "$EUID" -ne 0 ]; then
    print_error "This script must be run with sudo for installing PostgreSQL extensions"
    print_info "Usage: sudo ./scripts/03-install-extension.sh [$PRESET]"
    exit 1
fi

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

# Check if compiled extension exists
EXTENSION_SO="$BUILD_DIR/PostgresExtension/hartonomous.so"
if [ ! -f "$EXTENSION_SO" ]; then
    print_error "Extension library not found: $EXTENSION_SO"
    print_info "Run ./scripts/02-build-all.sh first"
    exit 1
fi

print_success "Found extension library: $EXTENSION_SO"

# Install extension files
print_step "Installing extension files..."

EXT_DIR="$SHAREDIR/extension"
mkdir -p "$EXT_DIR"

# Copy compiled extension to PostgreSQL lib directory
print_info "Installing hartonomous.so..."
cp "$EXTENSION_SO" "$PKGLIBDIR/hartonomous.so"
chmod 755 "$PKGLIBDIR/hartonomous.so"
print_success "Extension library installed: $PKGLIBDIR/hartonomous.so"

# Create hartonomous.control
print_info "Creating hartonomous.control..."
tee "$EXT_DIR/hartonomous.control" > /dev/null << EOF
# Hartonomous extension
comment = 'Hartonomous - Universal substrate for intelligence'
default_version = '0.1.0'
module_pathname = '\$libdir/hartonomous'
relocatable = true
requires = 'postgis'
EOF

# Create hartonomous--0.1.0.sql
print_info "Creating hartonomous--0.1.0.sql..."
tee "$EXT_DIR/hartonomous--0.1.0.sql" > /dev/null << 'EOF'
-- Hartonomous Extension SQL
-- This file defines the SQL interface to the C++ functions

-- Version function
CREATE OR REPLACE FUNCTION hartonomous_version()
RETURNS TEXT
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'hartonomous_version';

-- BLAKE3 hashing functions
CREATE OR REPLACE FUNCTION blake3_hash(TEXT)
RETURNS BYTEA
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'blake3_hash';

CREATE OR REPLACE FUNCTION blake3_hash_codepoint(INTEGER)
RETURNS BYTEA
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'blake3_hash_codepoint';

-- Codepoint projection functions
CREATE TYPE s3_point AS (
    x DOUBLE PRECISION,
    y DOUBLE PRECISION,
    z DOUBLE PRECISION,
    w DOUBLE PRECISION
);

CREATE OR REPLACE FUNCTION codepoint_to_s3(INTEGER)
RETURNS s3_point
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'codepoint_to_s3';

CREATE OR REPLACE FUNCTION codepoint_to_hilbert(INTEGER)
RETURNS BIGINT
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'codepoint_to_hilbert';

-- Centroid computation
CREATE OR REPLACE FUNCTION compute_centroid(s3_point[])
RETURNS s3_point
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'compute_centroid';

-- Ingestion stats type
CREATE TYPE ingestion_stats AS (
    atoms_new BIGINT,
    compositions_new BIGINT,
    relations_new BIGINT,
    original_bytes BIGINT,
    stored_bytes BIGINT,
    compression_ratio DOUBLE PRECISION
);

-- Text ingestion
CREATE OR REPLACE FUNCTION ingest_text(TEXT)
RETURNS ingestion_stats
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'ingest_text';

-- Semantic search
CREATE OR REPLACE FUNCTION semantic_search(TEXT)
RETURNS TABLE(result_text TEXT, confidence DOUBLE PRECISION)
LANGUAGE C STRICT
AS 'MODULE_PATHNAME', 'semantic_search';

EOF

print_success "Extension SQL file created"

# Test installation
print_step "Testing extension installation..."

PGDATABASE=${PGDATABASE:-hypercube}

# Use peer authentication (no password needed for local connections)
psql -U postgres -d "$PGDATABASE" << 'EOF'
-- Drop if exists
DROP EXTENSION IF EXISTS hartonomous CASCADE;

-- Create extension
CREATE EXTENSION hartonomous;

-- Test version function
SELECT hartonomous_version();
EOF

if [ $? -ne 0 ]; then
    print_error "Extension installation test failed"
    exit 1
fi

print_success "Extension installed and tested"

print_complete "Extension installation complete!"
echo ""
print_info "Extension installed in: $EXT_DIR"
print_info "Test in PostgreSQL:"
print_info "  CREATE EXTENSION hartonomous;"
print_info "  SELECT hartonomous_version();"
echo ""
