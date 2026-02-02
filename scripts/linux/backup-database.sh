#!/usr/bin/env bash
# Hartonomous Database Backup Script
# Usage: ./backup-database.sh [OPTIONS]

set -e

# Default values
DB_NAME="hypercube"
DB_USER="postgres"
DB_HOST="localhost"
DB_PORT="5432"
BACKUP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../" && pwd)/backups"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

function print_info() { echo -e "${CYAN}$1${NC}"; }
function print_success() { echo -e "${GREEN}$1${NC}"; }
function print_error() { echo -e "${RED}$1${NC}"; }

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--name) DB_NAME="$2"; shift 2 ;;
        -u|--user) DB_USER="$2"; shift 2 ;;
        -h|--host) DB_HOST="$2"; shift 2 ;;
        -p|--port) DB_PORT="$2"; shift 2 ;;
        -o|--output) BACKUP_DIR="$2"; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

mkdir -p "$BACKUP_DIR"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/${DB_NAME}_${TIMESTAMP}.sql.gz"

print_info "=== Hartonomous Database Backup ==="
print_info "Source DB: $DB_NAME"
print_info "Output:    $BACKUP_FILE"

# Use pg_dump with compression
# We exclude the 'ucd' schema if requested? No, backup everything for safety.
if pg_dump -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" "$DB_NAME" | gzip > "$BACKUP_FILE"; then
    print_success "✓ Backup completed successfully."
    # Create a 'latest' symlink
    ln -sf "$BACKUP_FILE" "$BACKUP_DIR/${DB_NAME}_latest.sql.gz"
else
    print_error "✗ Backup failed."
    exit 1
fi
