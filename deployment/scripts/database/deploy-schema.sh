#!/bin/bash
# Database Schema Deployment Script (Bash)
# Deploys PostgreSQL schema from schema/ directory
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Parse arguments
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"
DRY_RUN=false
SKIP_BACKUP=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --skip-backup)
            SKIP_BACKUP=true
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

write_step "Database Schema Deployment"

# Validate environment
if [[ -z "$ENVIRONMENT" ]]; then
    write_failure "DEPLOYMENT_ENVIRONMENT not set. Use -e parameter or set environment variable."
fi

# Validate environment value
case $ENVIRONMENT in
    development|staging|production)
        ;;
    *)
        write_failure "Invalid environment: $ENVIRONMENT (must be development, staging, or production)"
        ;;
esac

# Load configuration
load_deployment_config "$ENVIRONMENT"
write_log "Loaded configuration for: $ENVIRONMENT" "INFO"

# Get database configuration from JSON
DB_HOST=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.host')
DB_PORT=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.port')
DB_NAME=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.name')
DB_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.user')

write_log "Database: $DB_HOST:$DB_PORT/$DB_NAME" "INFO"

# Get database password
if [[ "$ENVIRONMENT" != "development" ]]; then
    write_step "Retrieving Database Credentials from Azure Key Vault"

    # Authenticate to Azure
    azure_login

    # Get Key Vault name
    KV_URL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.key_vault_url')
    KV_NAME=$(echo "$KV_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')

    # Get database password
    SECRET_NAME="PostgreSQL-$DB_NAME-Password"
    DB_PASSWORD=$(get_keyvault_secret "$KV_NAME" "$SECRET_NAME")
else
    # Development: Use environment variable
    DB_PASSWORD="${PGPASSWORD:-}"
    if [[ -z "$DB_PASSWORD" ]]; then
        write_failure "PGPASSWORD environment variable not set"
    fi
fi

# Set PostgreSQL environment variables
export PGHOST="$DB_HOST"
export PGPORT="$DB_PORT"
export PGDATABASE="$DB_NAME"
export PGUSER="$DB_USER"
export PGPASSWORD="$DB_PASSWORD"

# Backup database (unless skipped)
if [[ "$SKIP_BACKUP" != "true" ]]; then
    write_step "Creating Pre-Deployment Backup"
    "$SCRIPT_DIR/backup-database.sh" -e "$ENVIRONMENT"
fi

# Test database connectivity
write_step "Testing Database Connectivity"
if ! psql -t -c "SELECT version();" > /dev/null 2>&1; then
    write_failure "Database connection failed"
fi
write_success "Connected to PostgreSQL"

PG_VERSION=$(psql -t -c "SELECT version();" | xargs)
write_log "PostgreSQL version: $PG_VERSION" "DEBUG"

# Find schema files
write_step "Discovering Schema Files"
SCHEMA_ROOT="$SCRIPT_DIR/../../../schema"

if [[ ! -d "$SCHEMA_ROOT" ]]; then
    write_failure "Schema directory not found: $SCHEMA_ROOT"
fi

# Schema deployment order
SCHEMA_ORDER=(
    "core/tables"
    "core/indexes"
    "core/triggers"
    "core/functions"
    "extensions"
)

DEPLOYED_FILES=()

for SUB_PATH in "${SCHEMA_ORDER[@]}"; do
    SCHEMA_PATH="$SCHEMA_ROOT/$SUB_PATH"

    if [[ ! -d "$SCHEMA_PATH" ]]; then
        write_log "Schema path not found, skipping: $SUB_PATH" "WARNING"
        continue
    fi

    write_step "Deploying: $SUB_PATH"

    # Get SQL files in order
    SQL_FILES=($(find "$SCHEMA_PATH" -name "*.sql" -type f | sort))

    if [[ ${#SQL_FILES[@]} -eq 0 ]]; then
        write_log "No SQL files found in: $SUB_PATH" "WARNING"
        continue
    fi

    for FILE in "${SQL_FILES[@]}"; do
        FILENAME=$(basename "$FILE")
        write_log "Processing: $FILENAME" "INFO"

        if [[ "$DRY_RUN" == "true" ]]; then
            write_log "DRY RUN: Would deploy $FILE" "INFO"
            DEPLOYED_FILES+=("$FILE")
            continue
        fi

        # Execute SQL file
        write_log "Executing: $FILENAME" "DEBUG"
        if ! psql -f "$FILE" > /dev/null 2>&1; then
            write_failure "Failed to deploy: $FILENAME"
        fi

        write_success "Deployed: $FILENAME"
        DEPLOYED_FILES+=("$FILE")
    done
done

# Verify deployment
write_step "Verifying Deployment"

# Check tables
TABLE_COUNT=$(psql -t -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';" | xargs)
write_success "Tables: $TABLE_COUNT"

# Check functions
FUNC_COUNT=$(psql -t -c "SELECT COUNT(*) FROM pg_proc WHERE pronamespace = 'public'::regnamespace;" | xargs)
write_success "Functions: $FUNC_COUNT"

# Check triggers
TRIGGER_COUNT=$(psql -t -c "SELECT COUNT(*) FROM pg_trigger WHERE tgisinternal = false;" | xargs)
write_success "Triggers: $TRIGGER_COUNT"

# Summary
write_step "Deployment Summary"
write_success "Successfully deployed ${#DEPLOYED_FILES[@]} schema files"

if [[ "$DRY_RUN" == "true" ]]; then
    write_log "DRY RUN mode - no changes were made" "INFO"
fi

write_log "Database schema deployment completed" "INFO"
