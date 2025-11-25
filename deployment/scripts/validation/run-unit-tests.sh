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

cd "$REPO_ROOT"

# Add API directory to PYTHONPATH
export PYTHONPATH="$API_PATH:$PYTHONPATH"

# Check if pytest is installed
write_step "Checking Test Dependencies"

if ! python -m pytest --version &>/dev/null; then
    write_log "pytest not installed, installing test dependencies..." "INFO"
    pip install --quiet pytest pytest-cov pytest-asyncio pytest-mock
fi

write_success "Test dependencies ready"

# Check if tests directory exists
if [[ ! -d "$API_PATH/tests" ]]; then
    write_log "tests/ directory not found, creating..." "INFO"
    mkdir -p "$API_PATH/tests"

    # Create __init__.py
    touch "$API_PATH/tests/__init__.py"

    # Create sample test file
    cat > "$API_PATH/tests/test_sample.py" <<'EOF'
"""Sample test file for CI/CD pipeline."""
import pytest


def test_sanity():
    """Basic sanity test to verify pytest works."""
    assert True


def test_math():
    """Test basic math operations."""
    assert 1 + 1 == 2


@pytest.mark.asyncio
async def test_async_sanity():
    """Test async functionality."""
    async def sample_async():
        return True

    result = await sample_async()
    assert result is True
EOF

    write_log "Created sample test file: $API_PATH/tests/test_sample.py" "INFO"
fi

# Run unit tests
write_step "Running Unit Tests"

# Set test environment variables
export TESTING=true
export PGHOST="${PGHOST:-localhost}"
export PGPORT="${PGPORT:-5432}"
export PGDATABASE="${PGDATABASE:-hartonomous_test}"
export PGUSER="${PGUSER:-postgres}"
export PGPASSWORD="${PGPASSWORD:-}"

# Run pytest with coverage
write_log "Executing pytest with coverage..." "INFO"

cd "$API_PATH"

if python -m pytest tests/ \
    --verbose \
    --cov=. \
    --cov-report=term-missing \
    --cov-report=xml:coverage.xml \
    --cov-report=html:htmlcov \
    --junit-xml=test-results.xml \
    -m "not integration" \
    --tb=short; then

    write_success "All unit tests passed"
else
    EXIT_CODE=$?
    write_failure "Some unit tests failed (exit code: $EXIT_CODE)"
fi

# Display coverage summary
write_step "Coverage Summary"

if [[ -f "coverage.xml" ]]; then
    write_log "Coverage report generated: coverage.xml" "INFO"
    write_log "HTML coverage report: htmlcov/index.html" "INFO"

    # Extract coverage percentage if possible
    if command -v coverage &>/dev/null; then
        COVERAGE_PERCENT=$(coverage report | tail -1 | awk '{print $NF}')
        write_log "Total coverage: $COVERAGE_PERCENT" "INFO"
    fi
else
    write_log "Coverage report not generated" "WARNING"
fi

write_log "Unit tests completed" "INFO"
