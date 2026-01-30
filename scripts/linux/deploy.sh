#!/usr/bin/env bash
# Complete deployment script for Hartonomous on Linux

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/common.sh"

print_header "Hartonomous Complete Deployment"

# Parse options
PRESET="${1:-linux-release-max-perf}"
SKIP_BUILD="${SKIP_BUILD:-0}"
SKIP_DB="${SKIP_DB:-0}"

print_info "Configuration:"
print_info "  Preset: $PRESET"
print_info "  Skip build: $SKIP_BUILD"
print_info "  Skip database: $SKIP_DB"
echo ""

# Step 1: Build C++ code
if [ "$SKIP_BUILD" -eq 0 ]; then
    print_step "Step 1/3: Building C++ code..."
    if ! "$SCRIPT_DIR/02-build-all.sh" "$PRESET"; then
        print_error "Build failed"
        exit 1
    fi
    print_success "Build complete"
else
    print_warning "Skipping build (SKIP_BUILD=1)"
fi

echo ""

# Step 2: Install PostgreSQL extension (requires sudo)
print_step "Step 2/3: Installing PostgreSQL extension..."
if [ "$EUID" -ne 0 ]; then
    print_warning "Not running as root - will need sudo for extension installation"
    if ! sudo "$SCRIPT_DIR/03-install-extension.sh" "$PRESET"; then
        print_error "Extension installation failed"
        exit 1
    fi
else
    if ! "$SCRIPT_DIR/03-install-extension.sh" "$PRESET"; then
        print_error "Extension installation failed"
        exit 1
    fi
fi
print_success "Extension installed"

echo ""

# Step 3: Setup database
if [ "$SKIP_DB" -eq 0 ]; then
    print_step "Step 3/3: Setting up database..."
    if ! "$SCRIPT_DIR/01-rebuild-database.sh"; then
        print_error "Database setup failed"
        exit 1
    fi
    print_success "Database ready"
else
    print_warning "Skipping database setup (SKIP_DB=1)"
fi

echo ""

print_complete "Deployment Complete!"
echo ""
print_info "Summary:"
print_info "  ✓ C++ Engine and Extension built"
print_info "  ✓ PostgreSQL extension installed"
print_info "  ✓ Database 'hypercube' ready"
echo ""
print_info "Test the installation:"
print_info "  psql -d hypercube -c \"SELECT hartonomous_version();\""
print_info "  psql -d hypercube -c \"SELECT blake3_hash('Hello, World!');\""
echo ""
print_info "Environment:"
print_info "  PGHOST=${PGHOST:-localhost}"
print_info "  PGPORT=${PGPORT:-5432}"
print_info "  PGDATABASE=hypercube"
print_info "  PGUSER=${PGUSER:-$USER}"
echo ""
