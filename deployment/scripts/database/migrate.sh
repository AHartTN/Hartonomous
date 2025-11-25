#!/usr/bin/env bash
# Database Migration Script (Alembic-based)
# Idempotent, versioned, rollback-capable database migrations
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Parse arguments
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"
ACTION="upgrade"
TARGET="head"
DRY_RUN=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        --action)
            ACTION="$2"  # upgrade, downgrade, current, history
            shift 2
            ;;
        --target)
            TARGET="$2"  # head, +1, -1, or specific revision
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Database Migration (Alembic)"

# Validate environment
if [[ -z "$ENVIRONMENT" ]]; then
    write_failure "DEPLOYMENT_ENVIRONMENT not set"
fi

# Load configuration
load_deployment_config "$ENVIRONMENT"
write_log "Environment: $ENVIRONMENT" "INFO"

# Database configuration
DB_HOST=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.host')
DB_PORT=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.port')
DB_NAME=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.name')
DB_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.user')

write_log "Database: $DB_HOST:$DB_PORT/$DB_NAME" "INFO"

# Get database password
if [[ "$ENVIRONMENT" != "development" ]]; then
    write_step "Retrieving Database Credentials"
    azure_login
    
    KV_URL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.key_vault_url')
    KV_NAME=$(echo "$KV_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')
    SECRET_NAME="PostgreSQL-$DB_NAME-Password"
    
    DB_PASSWORD=$(get_keyvault_secret "$KV_NAME" "$SECRET_NAME")
else
    DB_PASSWORD="${PGPASSWORD:-}"
    if [[ -z "$DB_PASSWORD" ]]; then
        write_failure "PGPASSWORD not set"
    fi
fi

# Build connection string
export DATABASE_URL="postgresql://${DB_USER}:${DB_PASSWORD}@${DB_HOST}:${DB_PORT}/${DB_NAME}"

# Test connectivity
write_step "Testing Database Connectivity"
export PGPASSWORD="$DB_PASSWORD"
if ! psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT 1;" > /dev/null 2>&1; then
    write_failure "Database connection failed"
fi
write_success "Connected to PostgreSQL"

# Setup isolated Python environment
write_step "Setting Up Isolated Python Environment"

VENV_DIR="$REPO_ROOT/.deployment-venv"

# Create venv if it doesn't exist
if [[ ! -d "$VENV_DIR" ]]; then
    write_log "Creating virtual environment..." "INFO"
    python3 -m venv "$VENV_DIR"
fi

# Activate venv
if [[ -f "$VENV_DIR/bin/activate" ]]; then
    source "$VENV_DIR/bin/activate"
elif [[ -f "$VENV_DIR/Scripts/activate" ]]; then
    source "$VENV_DIR/Scripts/activate"
else
    write_failure "Failed to activate virtual environment"
fi

write_success "Virtual environment active"

# Install/upgrade Alembic in isolated environment
write_step "Installing Migration Tools (Isolated)"

if ! python -c "import alembic" &> /dev/null; then
    write_log "Installing alembic..." "INFO"
    pip install --quiet alembic psycopg[binary]
else
    write_log "Alembic already installed" "DEBUG"
fi

write_success "Migration tools ready"

# Change to repo root for Alembic
cd "$REPO_ROOT"

# Check Alembic configuration
if [[ ! -f "alembic.ini" ]]; then
    write_failure "alembic.ini not found in $REPO_ROOT"
fi

# Show current migration status
write_step "Current Migration Status"

CURRENT_VERSION=$(alembic current 2>/dev/null | grep -oP '(?<=\()[a-z0-9]+(?=\))' || echo "none")
write_log "Current version: $CURRENT_VERSION" "INFO"

# Perform migration action
write_step "Performing Migration: $ACTION $TARGET"

if [[ "$DRY_RUN" == "true" ]]; then
    write_log "DRY RUN MODE - No changes will be made" "WARNING"
    
    case $ACTION in
        upgrade)
            alembic upgrade "$TARGET" --sql
            ;;
        downgrade)
            alembic downgrade "$TARGET" --sql
            ;;
        current)
            alembic current --verbose
            ;;
        history)
            alembic history --verbose
            ;;
        *)
            write_failure "Unknown action: $ACTION"
            ;;
    esac
    
    write_log "DRY RUN complete - review SQL above" "INFO"
else
    case $ACTION in
        upgrade)
            write_log "Upgrading to: $TARGET" "INFO"
            alembic upgrade "$TARGET"
            write_success "Migration upgrade complete"
            ;;
        downgrade)
            write_log "Downgrading to: $TARGET" "INFO"
            alembic downgrade "$TARGET"
            write_success "Migration downgrade complete"
            ;;
        current)
            alembic current --verbose
            ;;
        history)
            alembic history --verbose
            ;;
        *)
            write_failure "Unknown action: $ACTION"
            ;;
    esac
fi

# Show new status
if [[ "$ACTION" == "upgrade" || "$ACTION" == "downgrade" ]]; then
    write_step "New Migration Status"
    NEW_VERSION=$(alembic current 2>/dev/null | grep -oP '(?<=\()[a-z0-9]+(?=\))' || echo "none")
    write_log "New version: $NEW_VERSION" "INFO"
fi

# Verify critical tables exist
write_step "Verifying Schema"

TABLE_COUNT=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c \
    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';" | xargs)
write_success "Tables: $TABLE_COUNT"

FUNC_COUNT=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c \
    "SELECT COUNT(*) FROM pg_proc WHERE pronamespace = 'public'::regnamespace;" | xargs)
write_success "Functions: $FUNC_COUNT"

# Cleanup
deactivate 2>/dev/null || true

write_step "Migration Complete"
write_success "Database schema version: $NEW_VERSION"

exit 0
