#!/bin/bash
# Unit Test Runner Script
# Runs pytest unit tests for Hartonomous API
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Unit Tests"

# Get repository root
REPO_ROOT="$SCRIPT_DIR/../../.."
API_PATH="$REPO_ROOT/api"

if [[ ! -d "$API_PATH" ]]; then
    write_failure "API directory not found: $API_PATH"
fi

cd "$API_PATH"

# Check if pytest is installed
write_step "Checking Test Dependencies"

if ! python3 -m pytest --version &>/dev/null; then
    write_log "pytest not installed, installing test dependencies..." "INFO"
    python3 -m pip install --quiet --upgrade pip
    python3 -m pip install --quiet pytest pytest-cov pytest-asyncio pytest-mock httpx
fi

write_success "Test dependencies ready"

# Verify tests directory exists and has actual tests
if [[ ! -d "tests" ]]; then
    write_failure "tests/ directory not found. Create tests before running unit tests."
fi

if ! find tests -name "test_*.py" -type f | grep -q .; then
    write_failure "No test files found in tests/. Create test_*.py files before running."
fi

# Count test files
TEST_FILE_COUNT=$(find tests -name "test_*.py" -type f | wc -l)
write_log "Found $TEST_FILE_COUNT test files" "INFO"

# Run unit tests
write_step "Running Unit Tests"

# Set test environment variables
export TESTING=true
export PGHOST="${PGHOST:-localhost}"
export PGPORT="${PGPORT:-5432}"
export PGDATABASE="${PGDATABASE:-hartonomous_test}"
export PGUSER="${PGUSER:-postgres}"
export PGPASSWORD="${PGPASSWORD:-}"
export NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}"
export NEO4J_USER="${NEO4J_USER:-neo4j}"
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-testpassword}"

# Run pytest with coverage
write_log "Executing pytest with coverage..." "INFO"

PYTEST_ARGS=(
    "tests/"
    "--verbose"
    "--cov=."
    "--cov-report=term-missing"
    "--cov-report=xml:coverage.xml"
    "--cov-report=html:htmlcov"
    "--junit-xml=test-results.xml"
    "-m" "not integration"
    "--tb=short"
    "--strict-markers"
    "--maxfail=5"
)

if python3 -m pytest "${PYTEST_ARGS[@]}"; then
    write_success "All unit tests passed"
    EXIT_CODE=0
else
    EXIT_CODE=$?
    write_log "Some unit tests failed (exit code: $EXIT_CODE)" "ERROR"
fi

# Display coverage summary
write_step "Coverage Summary"

if [[ -f "coverage.xml" ]]; then
    write_log "Coverage report generated: coverage.xml" "INFO"
    write_log "HTML coverage report: htmlcov/index.html" "INFO"

    # Extract coverage percentage
    if command -v python3 &>/dev/null; then
        COVERAGE_PERCENT=$(python3 -c "import xml.etree.ElementTree as ET; tree = ET.parse('coverage.xml'); root = tree.getroot(); print(f\"{float(root.attrib['line-rate'])*100:.2f}%\")" 2>/dev/null || echo "N/A")
        if [[ "$COVERAGE_PERCENT" != "N/A" ]]; then
            write_log "Total coverage: $COVERAGE_PERCENT" "INFO"
            
            # Warn if coverage is below threshold
            COVERAGE_NUM=$(echo "$COVERAGE_PERCENT" | sed 's/%//')
            if (( $(echo "$COVERAGE_NUM < 70" | bc -l) )); then
                write_log "WARNING: Coverage is below 70% threshold" "WARNING"
            fi
        fi
    fi
else
    write_log "Coverage report not generated" "WARNING"
fi

if [[ $EXIT_CODE -eq 0 ]]; then
    write_success "Unit tests completed successfully"
else
    write_failure "Unit tests failed"
fi

exit $EXIT_CODE
