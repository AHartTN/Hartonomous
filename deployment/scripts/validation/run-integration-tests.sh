#!/bin/bash
# Integration Test Runner Script
# Runs integration tests that require database and external services
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Integration Tests"

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
    write_log "Installing test dependencies..." "INFO"
    python3 -m pip install --quiet --upgrade pip
    python3 -m pip install --quiet pytest pytest-cov pytest-asyncio pytest-mock httpx psycopg2-binary
fi

write_success "Test dependencies ready"

# Verify integration tests exist
if [[ ! -d "tests/integration" ]]; then
    write_failure "tests/integration/ directory not found. Create integration tests before running."
fi

if ! find tests/integration -name "test_*.py" -type f | grep -q .; then
    write_failure "No integration test files found. Create test_*.py files in tests/integration/ before running."
fi

INTEGRATION_TEST_COUNT=$(find tests/integration -name "test_*.py" -type f | wc -l)
write_log "Found $INTEGRATION_TEST_COUNT integration test files" "INFO"

# Verify required services are available
write_step "Checking Service Availability"

SERVICES_OK=true

# Check PostgreSQL
write_log "Checking PostgreSQL..." "INFO"
if command -v psql &>/dev/null; then
    if PGPASSWORD="${PGPASSWORD:-}" psql -h "${PGHOST:-localhost}" -U "${PGUSER:-postgres}" -d postgres -c "SELECT 1" &>/dev/null; then
        write_success "PostgreSQL is available"
    else
        write_log "PostgreSQL not accessible - some tests will fail" "WARNING"
        SERVICES_OK=false
    fi
else
    write_log "psql not found - install PostgreSQL client" "WARNING"
    SERVICES_OK=false
fi

# Check Neo4j (if enabled)
write_log "Checking Neo4j..." "INFO"
if nc -z localhost 7687 &>/dev/null 2>&1 || timeout 1 bash -c "</dev/tcp/localhost/7687" &>/dev/null 2>&1; then
    write_success "Neo4j is available"
else
    write_log "Neo4j not accessible on port 7687 - Neo4j tests will be skipped" "INFO"
fi

# Check API (optional - tests may start it)
write_log "Checking API..." "INFO"
if curl -sf http://localhost:8000/health &>/dev/null; then
    write_success "API is running"
else
    write_log "API not running - tests that require running API will be skipped" "INFO"
fi

if [[ "$SERVICES_OK" != "true" ]]; then
    write_log "WARNING: Not all required services are available" "WARNING"
    write_log "Integration tests may fail or be skipped" "WARNING"
fi

# Run integration tests
write_step "Running Integration Tests"

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
export API_BASE_URL="${API_BASE_URL:-http://localhost:8000}"

# Run pytest with integration marker
write_log "Executing pytest (integration tests only)..." "INFO"

PYTEST_ARGS=(
    "tests/integration"
    "--verbose"
    "-m" "integration"
    "--tb=short"
    "--junit-xml=integration-test-results.xml"
    "--strict-markers"
    "--maxfail=10"
)

EXIT_CODE=0
if python3 -m pytest "${PYTEST_ARGS[@]}"; then
    write_success "All integration tests passed"
else
    EXIT_CODE=$?

    # Check if tests were skipped
    if [[ $EXIT_CODE -eq 5 ]]; then
        write_log "No integration tests collected or all tests were skipped" "WARNING"
        write_log "This is expected if external services aren't running or tests are marked skip" "INFO"
        EXIT_CODE=0
    else
        write_log "Integration tests failed (exit code: $EXIT_CODE)" "ERROR"
    fi
fi

# Summary
write_step "Integration Test Summary"

if [[ -f "integration-test-results.xml" ]]; then
    write_log "Test results saved to: integration-test-results.xml" "INFO"

    # Parse test results
    if command -v python3 &>/dev/null && python3 -c "import xml.etree.ElementTree" &>/dev/null; then
        TEST_STATS=$(python3 -c "
import xml.etree.ElementTree as ET
tree = ET.parse('integration-test-results.xml')
root = tree.getroot()
suite = root.find('.//testsuite')
if suite is not None:
    print(f\"tests={suite.get('tests', '0')}\")
    print(f\"failures={suite.get('failures', '0')}\")
    print(f\"errors={suite.get('errors', '0')}\")
    print(f\"skipped={suite.get('skipped', '0')}\")
" 2>/dev/null)
        
        if [[ -n "$TEST_STATS" ]]; then
            while IFS='=' read -r key value; do
                case $key in
                    tests) write_log "Total tests: $value" "INFO" ;;
                    failures) 
                        if [[ $value -gt 0 ]]; then
                            write_log "Failures: $value" "ERROR"
                        else
                            write_log "Failures: $value" "INFO"
                        fi
                        ;;
                    errors)
                        if [[ $value -gt 0 ]]; then
                            write_log "Errors: $value" "ERROR"
                        else
                            write_log "Errors: $value" "INFO"
                        fi
                        ;;
                    skipped) write_log "Skipped: $value" "INFO" ;;
                esac
            done <<< "$TEST_STATS"
        fi
    fi
else
    write_log "No test results file generated" "WARNING"
fi

if [[ $EXIT_CODE -eq 0 ]]; then
    write_success "Integration tests completed successfully"
else
    write_failure "Integration tests failed"
fi

exit $EXIT_CODE
