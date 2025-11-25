#!/bin/bash
# Database Backup Script (Bash)
# Creates timestamped backup of PostgreSQL database
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Parse arguments
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"
BACKUP_PATH=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        -p|--path)
            BACKUP_PATH="$2"
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

write_step "Database Backup"

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

# Construct backup directory
if [[ -z "$BACKUP_PATH" ]]; then
    BACKUP_PATH="$SCRIPT_DIR/../../../backups/database"
fi

if [[ ! -d "$BACKUP_PATH" ]]; then
    mkdir -p "$BACKUP_PATH"
    write_log "Created backup directory: $BACKUP_PATH" "INFO"
fi

# Generate backup filename with timestamp
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
DB_NAME=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.name')
BACKUP_FILE="$BACKUP_PATH/$DB_NAME-$ENVIRONMENT-$TIMESTAMP.sql"

write_log "Backup file: $BACKUP_FILE" "INFO"

# Get database configuration
DB_HOST=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.host')
DB_PORT=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.port')
DB_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.user')

# Get database password
if [[ "$ENVIRONMENT" != "development" ]]; then
    # Production/Staging: Get from Azure Key Vault
    azure_login

    KV_URL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.key_vault_url')
    KV_NAME=$(echo "$KV_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')
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

# Create backup using pg_dump
write_step "Creating Backup"
write_log "Running pg_dump..." "DEBUG"

# Use pg_dump with custom format for better compression
if ! pg_dump -F c -b -v -f "$BACKUP_FILE" "$DB_NAME"; then
    write_failure "Backup failed"
fi

write_success "Backup created: $BACKUP_FILE"

# Verify backup file
if [[ ! -f "$BACKUP_FILE" ]]; then
    write_failure "Backup file not found after creation: $BACKUP_FILE"
fi

FILE_SIZE=$(stat -c%s "$BACKUP_FILE" 2>/dev/null || stat -f%z "$BACKUP_FILE" 2>/dev/null)
FILE_SIZE_MB=$(echo "scale=2; $FILE_SIZE / 1048576" | bc)
write_success "Backup size: ${FILE_SIZE_MB} MB"

# Retention policy: Keep last 10 backups per environment
write_step "Applying Retention Policy"

# Find all backups for this environment and sort by modification time
mapfile -t ALL_BACKUPS < <(find "$BACKUP_PATH" -name "$DB_NAME-$ENVIRONMENT-*.sql" -type f -printf '%T@ %p\n' | sort -rn | cut -d' ' -f2-)

KEEP_COUNT=10
if [[ ${#ALL_BACKUPS[@]} -gt $KEEP_COUNT ]]; then
    # Delete old backups beyond retention count
    for ((i=$KEEP_COUNT; i<${#ALL_BACKUPS[@]}; i++)); do
        OLD_BACKUP="${ALL_BACKUPS[$i]}"
        write_log "Removing old backup: $(basename "$OLD_BACKUP")" "INFO"
        rm -f "$OLD_BACKUP"
    done

    DELETED_COUNT=$((${#ALL_BACKUPS[@]} - KEEP_COUNT))
    write_success "Retained $KEEP_COUNT most recent backups, deleted $DELETED_COUNT old backups"
else
    write_log "Current backups: ${#ALL_BACKUPS[@]} (retention: $KEEP_COUNT)" "INFO"
fi

write_log "Database backup completed: $BACKUP_FILE" "INFO"
