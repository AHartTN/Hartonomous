#!/usr/bin/env bash
# ==============================================================================
# Common Utilities for Hartonomous Scripts
# ==============================================================================
# Shared functions for consistent output formatting and database operations.
# ==============================================================================

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Print functions
print_header() {
    echo ""
    echo -e "${MAGENTA}========================================${NC}"
    echo -e "${MAGENTA}$1${NC}"
    echo -e "${MAGENTA}========================================${NC}"
    echo ""
}

print_step() {
    echo -e "${CYAN}▶ $1${NC}"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

print_info() {
    echo -e "${BLUE}  $1${NC}"
}

print_complete() {
    echo ""
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}✓ $1${NC}"
    echo -e "${GREEN}========================================${NC}"
    echo ""
}

# Check if command exists
command_exists() {
    command -v "$1" &> /dev/null
}

# Check PostgreSQL connection
check_postgres() {
    local host=${1:-localhost}
    local port=${2:-5432}
    local user=${3:-postgres}

    if ! command_exists psql; then
        print_error "psql not found. Install PostgreSQL client."
        return 1
    fi

    if ! psql -h "$host" -p "$port" -U "$user" -d postgres -c "SELECT 1" &> /dev/null; then
        print_error "Cannot connect to PostgreSQL at $host:$port"
        return 1
    fi

    return 0
}

# Get repository root
get_repo_root() {
    git rev-parse --show-toplevel 2>/dev/null || echo "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
}
