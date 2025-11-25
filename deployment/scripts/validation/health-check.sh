#!/usr/bin/env bash
# Health Check Script (Bash)
# Validates deployment health after deployment
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../common/logger.sh
source "$SCRIPT_DIR/../common/logger.sh"
# shellcheck source=../common/config-loader.sh
source "$SCRIPT_DIR/../common/config-loader.sh"

# Initialize logger
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-development}"
initialize_logger "" "INFO"

write_step "Health Check - $ENVIRONMENT"

# Load configuration
CONFIG=$(get_deployment_config "$ENVIRONMENT")
API_PORT=$(echo "$CONFIG" | jq -r '.api.port // 8000')
API_URL="http://localhost:${API_PORT}"

# Health checks
declare -a HEALTH_CHECKS=()

# Check 1: API Health Endpoint
write_step "Checking API Health Endpoint"
if RESPONSE=$(curl -s -f -m 10 "$API_URL/health" 2>/dev/null); then
    STATUS=$(echo "$RESPONSE" | jq -r '.status // "unknown"')
    write_success "API Health: $STATUS"
    HEALTH_CHECKS+=("API_Health:PASS")
else
    write_log "API health check failed" "ERROR"
    HEALTH_CHECKS+=("API_Health:FAIL")
fi

# Check 2: Database Connection
write_step "Checking Database Connection"
if curl -s -f -m 10 "$API_URL/health/database" >/dev/null 2>&1; then
    write_success "Database: Connected"
    HEALTH_CHECKS+=("Database:PASS")
else
    write_log "Database health check failed" "ERROR"
    HEALTH_CHECKS+=("Database:FAIL")
fi

# Check 3: Neo4j Connection (if enabled)
NEO4J_ENABLED=$(echo "$CONFIG" | jq -r '.features.neo4j_enabled')
if [[ "$NEO4J_ENABLED" == "true" ]]; then
    write_step "Checking Neo4j Connection"
    if curl -s -f -m 10 "$API_URL/health/neo4j" >/dev/null 2>&1; then
        write_success "Neo4j: Connected"
        HEALTH_CHECKS+=("Neo4j:PASS")
    else
        write_log "Neo4j health check failed" "ERROR"
        HEALTH_CHECKS+=("Neo4j:FAIL")
    fi
fi

# Check 4: Application Metrics
write_step "Checking Application Metrics"
if curl -s -f -m 10 "$API_URL/metrics" >/dev/null 2>&1; then
    write_success "Metrics: Available"
    HEALTH_CHECKS+=("Metrics:PASS")
else
    write_log "Metrics endpoint failed (optional)" "WARNING"
    HEALTH_CHECKS+=("Metrics:SKIP")
fi

# Summary
write_step "Health Check Summary"

TOTAL=${#HEALTH_CHECKS[@]}
PASSED=0
FAILED=0

for check in "${HEALTH_CHECKS[@]}"; do
    STATUS=$(echo "$check" | cut -d: -f2)
    if [[ "$STATUS" == "PASS" ]]; then
        ((PASSED++))
    elif [[ "$STATUS" == "FAIL" ]]; then
        ((FAILED++))
    fi
done

echo ""
if [[ $FAILED -eq 0 ]]; then
    echo -e "${COLOR_GREEN}Results: $PASSED/$TOTAL checks passed${COLOR_RESET}"
else
    echo -e "${COLOR_YELLOW}Results: $PASSED/$TOTAL checks passed${COLOR_RESET}"
fi
echo ""

for check in "${HEALTH_CHECKS[@]}"; do
    NAME=$(echo "$check" | cut -d: -f1 | tr '_' ' ')
    STATUS=$(echo "$check" | cut -d: -f2)

    case "$STATUS" in
        PASS) SYMBOL="✅" ;;
        FAIL) SYMBOL="❌" ;;
        SKIP) SYMBOL="⏭️" ;;
    esac

    echo "$SYMBOL $NAME: $STATUS"
done

if [[ $FAILED -gt 0 ]]; then
    write_failure "Health check failed: $FAILED checks failed"
fi

write_success "All health checks passed"
exit 0
