#!/usr/bin/env bash
# Preflight Check - Prerequisites (Bash)
# Validates system prerequisites before deployment
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../common/logger.sh
source "$SCRIPT_DIR/../common/logger.sh"
# shellcheck source=../common/config-loader.sh
source "$SCRIPT_DIR/../common/config-loader.sh"
# shellcheck source=../common/azure-auth.sh
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Initialize logger
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"
LOG_LEVEL="${LOG_LEVEL:-INFO}"
initialize_logger "/var/log/hartonomous/preflight-$(date '+%Y%m%d-%H%M%S').log" "$LOG_LEVEL"

write_step "Preflight Checks - Prerequisites"

# Validate environment
if [[ -z "$ENVIRONMENT" ]]; then
    write_failure "DEPLOYMENT_ENVIRONMENT not set"
fi

# Load configuration
CONFIG=$(get_deployment_config "$ENVIRONMENT")
TARGET=$(get_deployment_target)

write_log "Environment: $ENVIRONMENT" "INFO"
write_log "Target: $(hostname)" "INFO"

# Check 1: Disk Space
write_step "Checking Disk Space"
AVAILABLE_GB=$(df -BG / | awk 'NR==2 {print $4}' | sed 's/G//')
REQUIRED_GB=10

if [[ $AVAILABLE_GB -lt $REQUIRED_GB ]]; then
    write_failure "Insufficient disk space: ${AVAILABLE_GB}GB free, ${REQUIRED_GB}GB required"
fi
write_success "Disk space: ${AVAILABLE_GB}GB available"

# Check 2: Python Installation
write_step "Checking Python Installation"
if command -v python3 &>/dev/null; then
    PYTHON_VERSION=$(python3 --version 2>&1)
    write_success "Python: $PYTHON_VERSION"

    # Check version
    PYTHON_MINOR=$(python3 -c 'import sys; print(sys.version_info.minor)')
    if [[ $PYTHON_MINOR -lt 10 ]]; then
        write_failure "Python 3.10+ required, found: $PYTHON_VERSION"
    fi
else
    write_failure "Python 3 not found"
fi

# Check 3: PostgreSQL
write_step "Checking PostgreSQL"
if command -v psql &>/dev/null; then
    PG_VERSION=$(psql --version)
    write_success "PostgreSQL client: $PG_VERSION"

    # Test connection
    if PGPASSWORD="test" psql -h localhost -U postgres -c "SELECT version();" &>/dev/null; then
        write_success "PostgreSQL: Connected"
    else
        write_log "PostgreSQL connection test failed (expected in CI)" "WARNING"
    fi
else
    write_log "PostgreSQL client not found (optional for development)" "WARNING"
fi

# Check 4: Neo4j Availability
write_step "Checking Neo4j"
NEO4J_ENABLED=$(echo "$CONFIG" | jq -r '.features.neo4j_enabled')
if [[ "$NEO4J_ENABLED" == "true" ]]; then
    # Check if Neo4j service is running
    if nc -z localhost 7687 &>/dev/null || timeout 1 bash -c "</dev/tcp/localhost/7687" &>/dev/null; then
        write_success "Neo4j: Port 7687 accessible"
    else
        write_log "Neo4j not running on port 7687" "WARNING"
    fi
    
    # Check if Neo4j Python driver is installed
    if python3 -c "import neo4j" &>/dev/null; then
        NEO4J_DRIVER_VERSION=$(python3 -c "import neo4j; print(neo4j.__version__)")
        write_success "Neo4j Python driver: v$NEO4J_DRIVER_VERSION"
    else
        write_failure "Neo4j Python driver not installed (required when neo4j_enabled=true)"
    fi
fi

# Check 5: Azure CLI
write_step "Checking Azure CLI"
if command -v az &>/dev/null; then
    AZ_VERSION=$(az version --output json | jq -r '."azure-cli"')
    write_success "Azure CLI: $AZ_VERSION"
else
    write_failure "Azure CLI not installed"
fi

# Check 6: Git
write_step "Checking Git"
if command -v git &>/dev/null; then
    GIT_VERSION=$(git --version)
    write_success "Git: $GIT_VERSION"
else
    write_log "Git not found (optional)" "WARNING"
fi

# Check 7: Network Connectivity
write_step "Checking Network Connectivity"
for host in "management.azure.com:443" "github.com:443"; do
    HOST_NAME=$(echo "$host" | cut -d: -f1)
    HOST_PORT=$(echo "$host" | cut -d: -f2)

    if nc -z "$HOST_NAME" "$HOST_PORT" &>/dev/null || timeout 1 bash -c "</dev/tcp/$HOST_NAME/$HOST_PORT" &>/dev/null; then
        write_success "$HOST_NAME: Connected"
    else
        write_log "$HOST_NAME: Connection failed" "WARNING"
    fi
done

# Check 8: Required Environment Variables
write_step "Checking Environment Variables"
test_environment_variables \
    DEPLOYMENT_ENVIRONMENT \
    AZURE_TENANT_ID \
    AZURE_CLIENT_ID \
    AZURE_CLIENT_SECRET

# Final Summary
write_step "Preflight Checks Complete"
write_success "All critical prerequisites validated"
write_log "System is ready for deployment" "INFO"

exit 0
