#!/bin/bash
# Smoke Test Script (Bash)
# Quick validation tests for deployed application
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"

# Parse arguments
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"

while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Smoke Tests"

# Validate environment
if [[ -z "$ENVIRONMENT" ]]; then
    write_failure "DEPLOYMENT_ENVIRONMENT not set. Use -e parameter or set environment variable."
fi

case $ENVIRONMENT in
    development|staging|production)
        ;;
    *)
        write_failure "Invalid environment: $ENVIRONMENT"
        ;;
esac

# Load configuration
load_deployment_config "$ENVIRONMENT"
write_log "Running smoke tests for: $ENVIRONMENT" "INFO"

# Get API configuration
API_HOST=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.api.host')
API_PORT=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.api.port')
API_URL="http://$API_HOST:$API_PORT"

write_log "API URL: $API_URL" "INFO"

TESTS_PASSED=0
TESTS_FAILED=0

# Test 1: API Health Endpoint
write_step "Test 1: API Health Endpoint"
if RESPONSE=$(curl -sf "$API_URL/health" 2>/dev/null); then
    STATUS=$(echo "$RESPONSE" | jq -r '.status')
    if [[ "$STATUS" == "healthy" ]]; then
        write_success "API is healthy"
        ((TESTS_PASSED++))
    else
        write_log "API returned unexpected status: $STATUS" "ERROR"
        ((TESTS_FAILED++))
    fi
else
    write_log "API health check failed" "ERROR"
    ((TESTS_FAILED++))
fi

# Test 2: Database Connectivity
write_step "Test 2: Database Connectivity"
if RESPONSE=$(curl -sf "$API_URL/health/database" 2>/dev/null); then
    STATUS=$(echo "$RESPONSE" | jq -r '.status')
    if [[ "$STATUS" == "connected" ]]; then
        write_success "Database is connected"
        ((TESTS_PASSED++))
    else
        write_log "Database connectivity check failed: $STATUS" "ERROR"
        ((TESTS_FAILED++))
    fi
else
    write_log "Database health check failed" "ERROR"
    ((TESTS_FAILED++))
fi

# Test 3: Neo4j Connectivity (if enabled)
NEO4J_ENABLED=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.features.neo4j_enabled')
if [[ "$NEO4J_ENABLED" == "true" ]]; then
    write_step "Test 3: Neo4j Connectivity"
    if RESPONSE=$(curl -sf "$API_URL/health/neo4j" 2>/dev/null); then
        STATUS=$(echo "$RESPONSE" | jq -r '.status')
        if [[ "$STATUS" == "connected" ]]; then
            write_success "Neo4j is connected"
            ((TESTS_PASSED++))
        else
            write_log "Neo4j connectivity check failed: $STATUS" "ERROR"
            ((TESTS_FAILED++))
        fi
    else
        write_log "Neo4j health check failed" "ERROR"
        ((TESTS_FAILED++))
    fi
else
    write_log "Neo4j is disabled, skipping test" "INFO"
fi

# Test 4: API Documentation Endpoint
write_step "Test 4: API Documentation"
if curl -sf "$API_URL/docs" -o /dev/null; then
    write_success "API documentation is accessible"
    ((TESTS_PASSED++))
else
    write_log "API documentation check failed" "ERROR"
    ((TESTS_FAILED++))
fi

# Test 5: Create Test Atom (basic functionality)
write_step "Test 5: Create Test Atom"
TEST_ATOM=$(cat <<EOF
{
    "text": "Smoke test atom - $(date '+%Y-%m-%d %H:%M:%S')",
    "metadata": {
        "test": true,
        "environment": "$ENVIRONMENT"
    }
}
EOF
)

if RESPONSE=$(curl -sf -X POST "$API_URL/v1/atoms" \
    -H "Content-Type: application/json" \
    -d "$TEST_ATOM" 2>/dev/null); then

    TEST_ATOM_ID=$(echo "$RESPONSE" | jq -r '.atom_id')
    if [[ -n "$TEST_ATOM_ID" && "$TEST_ATOM_ID" != "null" ]]; then
        write_success "Test atom created: $TEST_ATOM_ID"
        ((TESTS_PASSED++))
    else
        write_log "Test atom creation failed" "ERROR"
        ((TESTS_FAILED++))
    fi
else
    write_log "Create atom test failed" "ERROR"
    ((TESTS_FAILED++))
fi

# Test 6: Retrieve Test Atom
if [[ -n "${TEST_ATOM_ID:-}" ]]; then
    write_step "Test 6: Retrieve Test Atom"
    if RESPONSE=$(curl -sf "$API_URL/v1/atoms/$TEST_ATOM_ID" 2>/dev/null); then
        ATOM_ID=$(echo "$RESPONSE" | jq -r '.atom_id')
        if [[ "$ATOM_ID" == "$TEST_ATOM_ID" ]]; then
            write_success "Test atom retrieved successfully"
            ((TESTS_PASSED++))
        else
            write_log "Test atom retrieval failed" "ERROR"
            ((TESTS_FAILED++))
        fi
    else
        write_log "Retrieve atom test failed" "ERROR"
        ((TESTS_FAILED++))
    fi
fi

# Summary
write_step "Smoke Test Summary"
TOTAL_TESTS=$((TESTS_PASSED + TESTS_FAILED))

write_log "Total tests: $TOTAL_TESTS" "INFO"
write_log "Tests passed: $TESTS_PASSED" "INFO"
write_log "Tests failed: $TESTS_FAILED" "INFO"

if [[ $TESTS_FAILED -eq 0 ]]; then
    write_success "All smoke tests passed"
    exit 0
else
    write_failure "Some smoke tests failed ($TESTS_FAILED/$TOTAL_TESTS)"
fi
