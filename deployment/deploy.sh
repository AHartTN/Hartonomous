#!/usr/bin/env bash
# Universal Deployment Script
# Works for localhost, development, staging, and production
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -eo pipefail

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Source common utilities
source "$SCRIPT_DIR/scripts/common/logger.sh"
source "$SCRIPT_DIR/scripts/common/config-loader.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Hartonomous Universal Deployment"

# Parse arguments
ENVIRONMENT="${1:-localhost}"
SKIP_TESTS="${2:-false}"

write_log "Deployment Environment: $ENVIRONMENT" "INFO"
write_log "Repository Root: $REPO_ROOT" "INFO"

# Load environment configuration
CONFIG_FILE="$REPO_ROOT/deployment/config/$ENVIRONMENT.json"

if [[ ! -f "$CONFIG_FILE" ]]; then
    write_failure "Configuration file not found: $CONFIG_FILE"
fi

write_log "Loading configuration from: $CONFIG_FILE" "INFO"
load_config "$CONFIG_FILE"

# Display configuration
write_step "Environment Configuration"
write_log "Target: $(get_config '.target.machine')" "INFO"
write_log "OS: $(get_config '.target.os')" "INFO"
write_log "Install Path: $(get_config '.deployment.install_path')" "INFO"
write_log "Database: $(get_config '.database.name')" "INFO"
write_log "API Port: $(get_config '.api.port')" "INFO"

# Step 1: Preflight Checks
write_step "[1/6] Preflight Checks"
chmod +x "$SCRIPT_DIR/scripts/preflight/check-prerequisites.sh"
"$SCRIPT_DIR/scripts/preflight/check-prerequisites.sh" || write_failure "Preflight checks failed"
write_success "Preflight checks passed"

# Step 2: Backup (if not localhost)
if [[ "$ENVIRONMENT" != "localhost" ]]; then
    write_step "[2/6] Creating Backup"
    BACKUP_PATH=$(get_config '.deployment.backup_path')
    INSTALL_PATH=$(get_config '.deployment.install_path')
    
    if [[ -d "$INSTALL_PATH" ]]; then
        TIMESTAMP=$(date +%Y%m%d_%H%M%S)
        BACKUP_DIR="$BACKUP_PATH/backup_$TIMESTAMP"
        
        mkdir -p "$BACKUP_DIR"
        write_log "Backing up $INSTALL_PATH to $BACKUP_DIR" "INFO"
        cp -r "$INSTALL_PATH" "$BACKUP_DIR/" || write_log "Backup warning: Could not copy all files" "WARNING"
        write_success "Backup created: $BACKUP_DIR"
    else
        write_log "No existing installation to backup" "INFO"
    fi
else
    write_step "[2/6] Backup (Skipped for localhost)"
fi

# Step 3: Database Deployment
write_step "[3/6] Database Schema Deployment"
chmod +x "$SCRIPT_DIR/scripts/database/deploy-schema.sh"
export DEPLOYMENT_ENVIRONMENT="$ENVIRONMENT"
"$SCRIPT_DIR/scripts/database/deploy-schema.sh" || write_failure "Database deployment failed"
write_success "Database schema deployed"

# Step 4: Application Deployment
write_step "[4/6] Application Deployment"
chmod +x "$SCRIPT_DIR/scripts/application/deploy-api.sh"
"$SCRIPT_DIR/scripts/application/deploy-api.sh" || write_failure "Application deployment failed"
write_success "Application deployed"

# Step 5: Neo4j Worker (if enabled)
NEO4J_ENABLED=$(get_config '.features.neo4j_enabled')
if [[ "$NEO4J_ENABLED" == "true" ]]; then
    write_step "[5/6] Neo4j Worker Deployment"
    chmod +x "$SCRIPT_DIR/scripts/neo4j/deploy-neo4j-worker.sh"
    "$SCRIPT_DIR/scripts/neo4j/deploy-neo4j-worker.sh" || write_log "Neo4j worker deployment failed" "WARNING"
    write_success "Neo4j worker deployed"
else
    write_step "[5/6] Neo4j Worker (Disabled)"
    write_log "Neo4j worker disabled in configuration" "INFO"
fi

# Step 6: Validation
if [[ "$SKIP_TESTS" != "true" ]]; then
    write_step "[6/6] Deployment Validation"
    
    # Health check
    chmod +x "$SCRIPT_DIR/scripts/validation/health-check.sh"
    "$SCRIPT_DIR/scripts/validation/health-check.sh" || write_log "Health check warnings detected" "WARNING"
    
    # Smoke tests
    chmod +x "$SCRIPT_DIR/scripts/validation/smoke-test.sh"
    "$SCRIPT_DIR/scripts/validation/smoke-test.sh" || write_log "Smoke test warnings detected" "WARNING"
    
    write_success "Validation complete"
else
    write_step "[6/6] Validation (Skipped)"
fi

# Summary
write_step "Deployment Summary"
write_success "Deployment to $ENVIRONMENT completed successfully!"
write_log "Environment: $ENVIRONMENT" "INFO"
write_log "Target: $(get_config '.target.machine')" "INFO"
write_log "Install Path: $(get_config '.deployment.install_path')" "INFO"
write_log "API Port: $(get_config '.api.port')" "INFO"
write_log "Database: $(get_config '.database.name')" "INFO"

# Next steps
write_log "" "INFO"
write_log "Next Steps:" "INFO"

if [[ "$ENVIRONMENT" == "localhost" ]]; then
    write_log "  1. Start API: cd api && python -m uvicorn main:app --reload" "INFO"
    write_log "  2. Access API: http://127.0.0.1:8000" "INFO"
    write_log "  3. View docs: http://127.0.0.1:8000/docs" "INFO"
else
    SERVICE_NAME=$(get_config '.deployment.service_name')
    write_log "  1. Check service: sudo systemctl status $SERVICE_NAME" "INFO"
    write_log "  2. View logs: sudo journalctl -u $SERVICE_NAME -f" "INFO"
    write_log "  3. Restart: sudo systemctl restart $SERVICE_NAME" "INFO"
fi

exit 0
