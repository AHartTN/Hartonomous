#!/bin/bash
# ============================================================================
# Hartonomous Database Initialization (Linux/macOS)
# Author: Anthony Hart
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.
# ============================================================================

set -e

PG_HOST="${PG_HOST:-localhost}"
PG_PORT="${PG_PORT:-5432}"
PG_USER="${PG_USER:-postgres}"
PG_DATABASE="${PG_DATABASE:-hartonomous}"
SCHEMA_PATH="${SCHEMA_PATH:-../../schema}"

CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
GRAY='\033[0;37m'
NC='\033[0m'

print_section() {
    echo -e "\n${CYAN}============================================${NC}"
    echo -e "${CYAN}$1${NC}"
    echo -e "${CYAN}============================================${NC}"
}

execute_sql_file() {
    if [ ! -f "$1" ]; then
        echo -e "${YELLOW}  WARNING: $1 not found${NC}"
        return
    fi
    echo -e "${GRAY}  $2${NC}"
    psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DATABASE" -f "$1" -v ON_ERROR_STOP=1 > /dev/null 2>&1
}

execute_sql_directory() {
    if [ ! -d "$1" ]; then
        echo -e "${YELLOW}  WARNING: $1 not found${NC}"
        return
    fi
    print_section "$2"
    for file in "$1"/*.sql; do
        [ -f "$file" ] && execute_sql_file "$file" "$(basename "$file")"
    done
    echo -e "${GREEN}  ? Complete${NC}"
}

# Main execution
print_section "Hartonomous Database Initialization"

# Extensions
execute_sql_directory "$SCHEMA_PATH/extensions" "Extensions"

# Custom Types
execute_sql_directory "$SCHEMA_PATH/types" "Custom Types"

# Tables
execute_sql_directory "$SCHEMA_PATH/core/tables" "Tables"

# Spatial Indexes
execute_sql_directory "$SCHEMA_PATH/core/indexes/spatial" "Spatial Indexes"

# Core Indexes
execute_sql_directory "$SCHEMA_PATH/core/indexes/core" "Core Indexes"

# Composition Indexes
execute_sql_directory "$SCHEMA_PATH/core/indexes/composition" "Composition Indexes"

# Relation Indexes
execute_sql_directory "$SCHEMA_PATH/core/indexes/relations" "Relation Indexes"

# Triggers
execute_sql_directory "$SCHEMA_PATH/core/triggers" "Triggers"

# AGE Provenance Graph
execute_sql_directory "$SCHEMA_PATH/age" "AGE Provenance Graph"

# Functions
execute_sql_directory "$SCHEMA_PATH/core/functions/helpers" "Helper Functions"
execute_sql_directory "$SCHEMA_PATH/core/functions/atomization" "Atomization Functions"
execute_sql_directory "$SCHEMA_PATH/core/functions/spatial" "Spatial Functions"
execute_sql_directory "$SCHEMA_PATH/core/functions/composition" "Composition Functions"
execute_sql_directory "$SCHEMA_PATH/core/functions/relations" "Relation Functions"
execute_sql_directory "$SCHEMA_PATH/core/functions/provenance" "Provenance Functions"
execute_sql_directory "$SCHEMA_PATH/core/functions/ooda" "OODA Functions"

# Views
execute_sql_directory "$SCHEMA_PATH/views" "Views"

print_section "Initialization Complete"
echo -e "${GREEN}? Hartonomous ready!${NC}"
echo -e "${GREEN}? CQRS: PostgreSQL (Command) + AGE (Query/Provenance)${NC}"
