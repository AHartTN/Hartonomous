#!/usr/bin/env bash
# Hartonomous Database Restore Script
# Usage: ./restore-database.sh [OPTIONS] [BACKUP_FILE]

set -e

# Default values
DB_NAME="hartonomous"
DB_USER="postgres"
DB_HOST="localhost"
DB_PORT="5432"
BACKUP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../" && pwd)/backups"
DROP_EXISTING=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

function print_info() { echo -e "${CYAN}$1${NC}"; }
function print_success() { echo -e "${GREEN}$1${NC}"; }
function print_error() { echo -e "${RED}$1${NC}"; }
function print_warning() { echo -e "${YELLOW}$1${NC}"; }

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--name) DB_NAME="$2"; shift 2 ;;
        -u|--user) DB_USER="$2"; shift 2 ;;
        -h|--host) DB_HOST="$2"; shift 2 ;;
        -p|--port) DB_PORT="$2"; shift 2 ;;
        -d|--drop) DROP_EXISTING=true; shift ;;
        *) 
            if [ -f "$1" ]; then
                BACKUP_FILE="$1"
                shift
            else
                echo "Unknown option or missing file: $1"
                exit 1
            fi
            ;;
    esac
done

# Default to latest if not specified
if [ -z "$BACKUP_FILE" ]; then
    BACKUP_FILE="$BACKUP_DIR/${DB_NAME}_latest.sql.gz"
fi

if [ ! -f "$BACKUP_FILE" ]; then
    print_error "✗ Backup file not found: $BACKUP_FILE"
    exit 1
fi

print_info "=== Hartonomous Database Restore ==="
print_info "Target DB: $DB_NAME"
print_info "Source:    $BACKUP_FILE"

# Drop existing if requested
if [ "$DROP_EXISTING" = true ]; then
    print_warning "Dropping existing database $DB_NAME..."
    psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "
        SELECT pg_terminate_backend(pid)
        FROM pg_stat_activity
        WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid();
    " 2>/dev/null || true
    sleep 0.5
    psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "DROP DATABASE IF EXISTS $DB_NAME;"
fi

# Create DB if it doesn't exist
EXISTS=$(psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DB_NAME'")
if [ "$EXISTS" != "1" ]; then
    print_info "Creating database $DB_NAME..."
    psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "CREATE DATABASE $DB_NAME;"
fi

# Restore
print_info "Restoring data..."
if gunzip -c "$BACKUP_FILE" | psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" > /dev/null; then
    print_success "✓ Restore completed successfully."
else
    print_error "✗ Restore failed."
    exit 1
fi
