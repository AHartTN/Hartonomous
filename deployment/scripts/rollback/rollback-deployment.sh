#!/bin/bash
# Rollback Deployment Script (Bash)
# Rolls back to previous deployment using backups
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Parse arguments
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"
DATABASE_BACKUP=""
APPLICATION_BACKUP=""
FORCE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        --database-backup)
            DATABASE_BACKUP="$2"
            shift 2
            ;;
        --application-backup)
            APPLICATION_BACKUP="$2"
            shift 2
            ;;
        --force)
            FORCE=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Deployment Rollback"

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
write_log "Rolling back deployment for: $ENVIRONMENT" "INFO"

# Safety check for production
if [[ "$ENVIRONMENT" == "production" && "$FORCE" != "true" ]]; then
    write_log "PRODUCTION rollback requires --force flag" "ERROR"
    write_log "This will restore database and application from backup" "WARNING"
    write_failure "Add --force flag to confirm production rollback"
fi

# Get backup directories
BACKUP_ROOT="$SCRIPT_DIR/../../../backups"
DB_BACKUP_PATH="$BACKUP_ROOT/database"
APP_BACKUP_PATH="$BACKUP_ROOT/application"

# Find latest backups if not specified
if [[ -z "$DATABASE_BACKUP" ]]; then
    write_step "Finding Latest Database Backup"

    DB_NAME=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.name')

    # Find most recent backup
    LATEST_DB_BACKUP=$(find "$DB_BACKUP_PATH" -name "$DB_NAME-$ENVIRONMENT-*.sql" -type f -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1 | cut -d' ' -f2- || true)

    if [[ -n "$LATEST_DB_BACKUP" ]]; then
        DATABASE_BACKUP="$LATEST_DB_BACKUP"
        write_log "Found database backup: $(basename "$DATABASE_BACKUP")" "INFO"
    else
        write_log "No database backup found for: $DB_NAME-$ENVIRONMENT" "WARNING"
    fi
fi

if [[ -z "$APPLICATION_BACKUP" ]]; then
    write_step "Finding Latest Application Backup"

    # Find most recent backup
    LATEST_APP_BACKUP=$(find "$APP_BACKUP_PATH" -name "api-$ENVIRONMENT-*.tar.gz" -type f -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1 | cut -d' ' -f2- || true)

    if [[ -n "$LATEST_APP_BACKUP" ]]; then
        APPLICATION_BACKUP="$LATEST_APP_BACKUP"
        write_log "Found application backup: $(basename "$APPLICATION_BACKUP")" "INFO"
    else
        write_log "No application backup found for: api-$ENVIRONMENT" "WARNING"
    fi
fi

# Confirm rollback
write_step "Rollback Confirmation"
echo "Environment: $ENVIRONMENT"
echo "Database Backup: $DATABASE_BACKUP"
echo "Application Backup: $APPLICATION_BACKUP"
echo ""

if [[ "$FORCE" != "true" ]]; then
    read -p "Proceed with rollback? (yes/no): " CONFIRMATION
    if [[ "$CONFIRMATION" != "yes" ]]; then
        write_log "Rollback cancelled by user" "INFO"
        exit 0
    fi
fi

# Stop application
write_step "Stopping Application"
SERVICE_NAME="hartonomous-api-$ENVIRONMENT"

if systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    write_log "Stopping service: $SERVICE_NAME" "INFO"
    sudo systemctl stop "$SERVICE_NAME"
    sleep 2
    write_success "Service stopped"
else
    write_log "Service not running: $SERVICE_NAME" "INFO"
fi

# Rollback Database
if [[ -n "$DATABASE_BACKUP" && -f "$DATABASE_BACKUP" ]]; then
    write_step "Rolling Back Database"

    # Get database configuration
    DB_HOST=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.host')
    DB_PORT=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.port')
    DB_NAME=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.name')
    DB_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.user')

    # Get database password
    if [[ "$ENVIRONMENT" != "development" ]]; then
        azure_login

        KV_URL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.key_vault_url')
        KV_NAME=$(echo "$KV_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')
        DB_PASSWORD=$(get_keyvault_secret "$KV_NAME" "PostgreSQL-$DB_NAME-Password")
    else
        DB_PASSWORD="${PGPASSWORD:-}"
    fi

    # Set PostgreSQL environment variables
    export PGHOST="$DB_HOST"
    export PGPORT="$DB_PORT"
    export PGDATABASE="$DB_NAME"
    export PGUSER="$DB_USER"
    export PGPASSWORD="$DB_PASSWORD"

    write_log "Restoring database from: $DATABASE_BACKUP" "INFO"

    # Restore database using pg_restore
    if pg_restore -c -d "$DB_NAME" "$DATABASE_BACKUP" 2>&1; then
        write_success "Database restored successfully"
    else
        write_log "Database restore had warnings (this may be normal)" "WARNING"
    fi
else
    write_log "No database backup to restore" "WARNING"
fi

# Rollback Application
if [[ -n "$APPLICATION_BACKUP" && -f "$APPLICATION_BACKUP" ]]; then
    write_step "Rolling Back Application"

    API_PATH="$SCRIPT_DIR/../../../api"
    TEMP_EXTRACT_PATH="/tmp/hartonomous-rollback-$(date +%Y%m%d-%H%M%S)"

    mkdir -p "$TEMP_EXTRACT_PATH"

    # Extract backup
    write_log "Extracting backup: $APPLICATION_BACKUP" "INFO"
    tar -xzf "$APPLICATION_BACKUP" -C "$TEMP_EXTRACT_PATH"

    # Remove current API directory (keep .venv)
    write_log "Removing current application files..." "INFO"
    find "$API_PATH" -mindepth 1 -maxdepth 1 ! -name ".venv" ! -name "__pycache__" -exec rm -rf {} +

    # Copy backup files
    write_log "Restoring application files..." "INFO"
    cp -r "$TEMP_EXTRACT_PATH/api/"* "$API_PATH/"

    # Clean up temp directory
    rm -rf "$TEMP_EXTRACT_PATH"

    write_success "Application restored successfully"
else
    write_log "No application backup to restore" "WARNING"
fi

# Restart application
write_step "Restarting Application"

if systemctl list-unit-files | grep -q "^$SERVICE_NAME"; then
    write_log "Starting service: $SERVICE_NAME" "INFO"
    sudo systemctl start "$SERVICE_NAME"
    sleep 3

    if systemctl is-active --quiet "$SERVICE_NAME"; then
        write_success "Service started successfully"
    else
        write_log "Service did not start successfully" "WARNING"
    fi
else
    write_log "Service not installed: $SERVICE_NAME" "INFO"
    write_log "For development, start manually: cd api && source .venv/bin/activate && python -m uvicorn main:app --reload" "INFO"
fi

# Run health checks
write_step "Running Health Checks"
"$SCRIPT_DIR/../validation/health-check.sh" -e "$ENVIRONMENT" || write_log "Health checks had warnings" "WARNING"

# Summary
write_step "Rollback Summary"
write_success "Deployment rollback completed"
write_log "Environment: $ENVIRONMENT" "INFO"
write_log "Database rolled back: $(if [[ -n "$DATABASE_BACKUP" ]]; then echo "Yes"; else echo "No"; fi)" "INFO"
write_log "Application rolled back: $(if [[ -n "$APPLICATION_BACKUP" ]]; then echo "Yes"; else echo "No"; fi)" "INFO"

write_log "Deployment rollback completed" "INFO"
