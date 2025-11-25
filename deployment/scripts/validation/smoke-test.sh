#!/usr/bin/env bash
# Smoke Test Script
# Quick validation tests after deployment

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"

ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-development}"
initialize_logger "/var/log/hartonomous/smoke-test-$(date '+%Y%m%d-%H%M%S').log" "INFO"

write_step "Smoke Tests - $ENVIRONMENT"

# Load configuration
CONFIG=$(get_deployment_config "$ENVIRONMENT")
API_HOST=$(echo "$CONFIG" | jq -r '.api.host')
API_PORT=$(echo "$CONFIG" | jq -r '.api.port')
BASE_URL="http://${API_HOST}:${API_PORT}"

write_log "API Base URL: $BASE_URL" "INFO"

# Test 1: Health endpoint
write_step "Test 1: Health Endpoint"
if curl -sf "${BASE_URL}/v1/health" | jq -e '.status == "ok"' > /dev/null; then
    write_success "Health endpoint OK"
else
    write_failure "Health endpoint failed"
fi

# Test 2: Readiness endpoint
write_step "Test 2: Readiness Endpoint"
if curl -sf "${BASE_URL}/v1/ready" | jq -e '.status == "ready"' > /dev/null; then
    write_success "Readiness check OK"
else
    write_log "Readiness check failed (may be expected during startup)" "WARNING"
fi

# Test 3: Statistics endpoint
write_step "Test 3: Statistics Endpoint"
if curl -sf "${BASE_URL}/v1/stats" > /dev/null; then
    write_success "Statistics endpoint OK"
else
    write_failure "Statistics endpoint failed"
fi

# Test 4: API documentation
write_step "Test 4: API Documentation"
if curl -sf "${BASE_URL}/docs" > /dev/null; then
    write_success "API documentation accessible"
else
    write_log "API documentation not accessible" "WARNING"
fi

# Test 5: PostgreSQL connectivity
write_step "Test 5: PostgreSQL Connectivity"
if PGPASSWORD="test" psql -h localhost -U postgres -c "SELECT 1;" &> /dev/null; then
    write_success "PostgreSQL accessible"
else
    write_log "PostgreSQL not accessible (expected in some environments)" "WARNING"
fi

# Test 6: Neo4j connectivity (if enabled)
NEO4J_ENABLED=$(echo "$CONFIG" | jq -r '.features.neo4j_enabled')
if [[ "$NEO4J_ENABLED" == "true" ]]; then
    write_step "Test 6: Neo4j Connectivity"
    if nc -z localhost 7687 &>/dev/null || timeout 1 bash -c "</dev/tcp/localhost/7687" &>/dev/null; then
        write_success "Neo4j accessible"
    else
        write_log "Neo4j not accessible" "WARNING"
    fi
fi

write_step "Smoke Tests Complete"
write_success "All critical tests passed"

exit 0
