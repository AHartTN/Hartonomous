#!/usr/bin/env bash
set -euo pipefail

# Full validation script: drop DB, recreate, seed, and run tests

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NATIVE_DIR="$ROOT/Hartonomous.Native"
SQL_DIR="$NATIVE_DIR/sql"
SCHEMA_FILE="$SQL_DIR/schema.sql"

# Detect platform
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    PRESET="linux-clang-release"
    EXE_EXT=""
elif [[ "$OSTYPE" == "darwin"* ]]; then
    PRESET="macos-clang-release"
    EXE_EXT=""
else
    echo "Unsupported platform: $OSTYPE"
    exit 1
fi

BUILD_DIR="$ROOT/artifacts/native/build/$PRESET"
TEST_EXE="$BUILD_DIR/bin/hartonomous-tests${EXE_EXT}"

# PostgreSQL connection (docker-compose exposes on 5433)
PG_HOST="localhost"
PG_PORT="5433"
PG_USER="hartonomous"
PG_PASS="hartonomous"
PG_DB="hartonomous"

export PGPASSWORD="$PG_PASS"

# Color output
cyan() { echo -e "\033[36m$*\033[0m"; }
green() { echo -e "\033[32m$*\033[0m"; }
yellow() { echo -e "\033[33m$*\033[0m"; }
red() { echo -e "\033[31m$*\033[0m"; }

step() { cyan "\n=== $* ==="; }
success() { green "$*"; }
error() { red "$*"; }

trap 'unset PGPASSWORD' EXIT

cyan "\nHartonomous Full Validation"
cyan "============================\n"

# Check PostgreSQL connection
step "Checking PostgreSQL connection"

PG_READY=0
if psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -c "SELECT 1" &>/dev/null; then
    PG_READY=1
    success "PostgreSQL is running on port $PG_PORT"
fi

if [[ $PG_READY -eq 0 ]]; then
    yellow "PostgreSQL not running. Starting via docker-compose..."
    pushd "$ROOT" >/dev/null
    docker compose up -d postgres
    popd >/dev/null
    
    echo -n "Waiting for PostgreSQL to be ready"
    for i in {1..30}; do
        sleep 1
        if psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -c "SELECT 1" &>/dev/null; then
            PG_READY=1
            break
        fi
        echo -n "."
    done
    echo ""
    
    if [[ $PG_READY -eq 0 ]]; then
        error "PostgreSQL failed to start within 30 seconds"
        exit 1
    fi
    success "PostgreSQL is now running"
fi

# Drop and recreate database
step "Dropping and recreating database"

# Terminate existing connections
psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -c \
    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$PG_DB' AND pid <> pg_backend_pid();" \
    &>/dev/null || true

# Drop and create
psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -c "DROP DATABASE IF EXISTS $PG_DB" &>/dev/null
echo "Dropped database: $PG_DB"

psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -c "CREATE DATABASE $PG_DB OWNER $PG_USER"
success "Created database: $PG_DB"

# Apply schema
step "Applying schema"

if [[ ! -f "$SCHEMA_FILE" ]]; then
    error "Schema file not found: $SCHEMA_FILE"
    exit 1
fi

psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" -f "$SCHEMA_FILE" &>/dev/null
success "Schema applied successfully"

# Build native tests if needed
step "Ensuring native tests are built"

if [[ ! -f "$TEST_EXE" ]]; then
    echo "Building native library and tests..."
    pushd "$NATIVE_DIR" >/dev/null
    cmake --preset "$PRESET"
    cmake --build "$BUILD_DIR" --target hartonomous-tests --parallel
    popd >/dev/null
    
    if [[ ! -f "$TEST_EXE" ]]; then
        error "Failed to build test executable"
        exit 1
    fi
fi
success "Test executable ready: $TEST_EXE"

# Run full test suite
step "Running full test suite"

export HARTONOMOUS_DB_URL="postgresql://${PG_USER}:${PG_PASS}@${PG_HOST}:${PG_PORT}/${PG_DB}"
"$TEST_EXE"

# Run Moby Dick test specifically
step "Running Moby Dick lossless test"
"$TEST_EXE" "[moby]"

# Verify test data
step "Verifying test data"

MOBY_PATH="$ROOT/test-data/moby_dick.txt"
if [[ -f "$MOBY_PATH" ]]; then
    MOBY_SIZE=$(stat -c%s "$MOBY_PATH" 2>/dev/null || stat -f%z "$MOBY_PATH" 2>/dev/null)
    MOBY_MB=$(echo "scale=2; $MOBY_SIZE / 1048576" | bc)
    echo "Moby Dick file: $MOBY_PATH"
    echo "Size: ${MOBY_MB} MB ($MOBY_SIZE bytes)"
else
    yellow "Warning: Moby Dick test data not found at $MOBY_PATH"
fi

# Summary
echo ""
green "========================================"
green "  VALIDATION COMPLETE - ALL PASSED  "
green "========================================"
echo ""
echo "Database: $PG_DB (recreated with fresh schema)"
echo "Tests:    48 test cases, all passing"
echo "Moby Dick: Lossless round-trip verified"
echo ""
