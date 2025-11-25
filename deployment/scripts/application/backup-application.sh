#!/bin/bash
# Application Backup Script (Bash)
# Creates timestamped backup of API application code
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"

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

write_step "Application Backup"

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
    BACKUP_PATH="$SCRIPT_DIR/../../../backups/application"
fi

if [[ ! -d "$BACKUP_PATH" ]]; then
    mkdir -p "$BACKUP_PATH"
    write_log "Created backup directory: $BACKUP_PATH" "INFO"
fi

# Get API path
REPO_ROOT="$SCRIPT_DIR/../../.."
API_PATH="$REPO_ROOT/api"

if [[ ! -d "$API_PATH" ]]; then
    write_failure "API directory not found: $API_PATH"
fi

# Generate backup filename with timestamp
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
BACKUP_FILE="$BACKUP_PATH/api-$ENVIRONMENT-$TIMESTAMP.tar.gz"

write_log "Backup file: $BACKUP_FILE" "INFO"

# Create backup archive
write_step "Creating Backup Archive"

write_log "Compressing API directory..." "DEBUG"

# Exclude patterns
EXCLUDE_PATTERNS=(
    ".venv"
    "__pycache__"
    "*.pyc"
    ".pytest_cache"
    ".env"
)

# Build tar exclude arguments
EXCLUDE_ARGS=()
for pattern in "${EXCLUDE_PATTERNS[@]}"; do
    EXCLUDE_ARGS+=("--exclude=$pattern")
done

# Create tar archive
if ! tar -czf "$BACKUP_FILE" "${EXCLUDE_ARGS[@]}" -C "$(dirname "$API_PATH")" "$(basename "$API_PATH")"; then
    write_failure "Failed to create backup archive"
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
mapfile -t ALL_BACKUPS < <(find "$BACKUP_PATH" -name "api-$ENVIRONMENT-*.tar.gz" -type f -printf '%T@ %p\n' | sort -rn | cut -d' ' -f2-)

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

write_log "Application backup completed: $BACKUP_FILE" "INFO"
