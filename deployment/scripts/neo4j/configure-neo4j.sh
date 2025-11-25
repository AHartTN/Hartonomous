#!/bin/bash
# Neo4j Configuration Script (Bash)
# Configures Neo4j connection settings
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

write_step "Neo4j Configuration"

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

# Check if Neo4j is enabled
NEO4J_ENABLED=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.features.neo4j_enabled')
if [[ "$NEO4J_ENABLED" != "true" ]]; then
    write_log "Neo4j is disabled for environment: $ENVIRONMENT" "INFO"
    exit 0
fi

write_log "Configuring Neo4j for: $ENVIRONMENT" "INFO"

# Get Neo4j settings
NEO4J_URI=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.uri')
NEO4J_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.user')
NEO4J_DATABASE=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.database')
NEO4J_EDITION=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.edition')

write_log "Neo4j Edition: $NEO4J_EDITION" "INFO"
write_log "Neo4j URI: $NEO4J_URI" "INFO"
write_log "Neo4j User: $NEO4J_USER" "INFO"
write_log "Neo4j Database: $NEO4J_DATABASE" "INFO"

# Validate Neo4j configuration
write_step "Validating Neo4j Configuration"

# Check if URI is valid
if [[ -z "$NEO4J_URI" || "$NEO4J_URI" == "null" ]]; then
    write_failure "Neo4j URI not configured in deployment config"
fi

if [[ ! "$NEO4J_URI" =~ ^bolt:// ]]; then
    write_failure "Neo4j URI must use bolt:// protocol, got: $NEO4J_URI"
fi

write_success "Neo4j configuration validated"

# Environment-specific checks
if [[ "$ENVIRONMENT" == "development" ]]; then
    write_log "Development environment: Using Neo4j Desktop edition" "INFO"
    write_log "Expected: bolt://localhost:7687" "INFO"

    if [[ "$NEO4J_URI" != "bolt://localhost:7687" ]]; then
        write_log "Warning: URI does not match expected development URI" "WARNING"
    fi
else
    write_log "Production/Staging environment: Using Neo4j Community edition" "INFO"

    KV_URL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.key_vault_url')
    if [[ "$KV_URL" != "null" ]]; then
        write_log "Credentials will be retrieved from Azure Key Vault" "INFO"
        write_log "Key Vault: $KV_URL" "INFO"
    else
        write_log "Warning: No Key Vault configured for Neo4j credentials" "WARNING"
    fi
fi

# Check Neo4j service status (if running locally)
if [[ "$NEO4J_URI" =~ localhost|127\.0\.0\.1 ]]; then
    write_step "Checking Neo4j Service Status"

    NEO4J_HOST="localhost"
    NEO4J_PORT=7687

    if nc -z "$NEO4J_HOST" "$NEO4J_PORT" &>/dev/null; then
        write_success "Neo4j is running and accepting connections"
    else
        write_log "Neo4j is not responding on port $NEO4J_PORT" "WARNING"
        if [[ "$ENVIRONMENT" == "development" ]]; then
            write_log "Please start Neo4j Desktop before running the application" "INFO"
        else
            write_log "Please ensure Neo4j service is running" "INFO"
        fi
    fi
fi

# Summary
write_step "Configuration Summary"
write_success "Neo4j configuration completed"
write_log "Environment: $ENVIRONMENT" "INFO"
write_log "Neo4j URI: $NEO4J_URI" "INFO"
write_log "Neo4j Database: $NEO4J_DATABASE" "INFO"

write_log "Neo4j configuration completed" "INFO"
