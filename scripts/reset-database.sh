#!/bin/bash
# ============================================================================
# Hartonomous Database Reset Script
# 
# WARNING: This will DROP and recreate the database, destroying all data!
#
# Usage:
#   ./scripts/reset-database.sh [--force]
#
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.
# ============================================================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# Load .env file
if [ -f "${PROJECT_ROOT}/.env" ]; then
    export $(grep -v '^#' "${PROJECT_ROOT}/.env" | xargs)
fi

# Database connection
PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-5432}"
PGUSER="${PGUSER:-hartonomous}"
PGDATABASE="${PGDATABASE:-hartonomous}"

echo -e "${RED}???????????????????????????????????????????????????????????${NC}"
echo -e "${RED}  DATABASE RESET WARNING${NC}"
echo -e "${RED}???????????????????????????????????????????????????????????${NC}"
echo ""
echo -e "${YELLOW}  This will:${NC}"
echo -e "${YELLOW}    1. DROP database: ${PGDATABASE}${NC}"
echo -e "${YELLOW}    2. CREATE new database${NC}"
echo -e "${YELLOW}    3. Run initialization scripts${NC}"
echo ""
echo -e "${RED}  ALL DATA WILL BE LOST!${NC}"
echo ""

if [ "${1}" != "--force" ]; then
    read -p "Type 'YES' to continue: " confirmation
    if [ "$confirmation" != "YES" ]; then
        echo -e "${YELLOW}?${NC} Cancelled"
        exit 0
    fi
fi

echo -e "${YELLOW}?${NC} Dropping database: ${PGDATABASE}"

# Drop database
if PGPASSWORD="${PGPASSWORD}" psql -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" -d postgres -c "DROP DATABASE IF EXISTS ${PGDATABASE};" > /dev/null 2>&1; then
    echo -e "${GREEN}?${NC} Database dropped"
else
    echo -e "${RED}?${NC} Failed to drop database"
    exit 1
fi

echo -e "${YELLOW}?${NC} Creating database: ${PGDATABASE}"

# Create database
if PGPASSWORD="${PGPASSWORD}" psql -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" -d postgres -c "CREATE DATABASE ${PGDATABASE} WITH OWNER ${PGUSER} ENCODING 'UTF8';" > /dev/null 2>&1; then
    echo -e "${GREEN}?${NC} Database created"
else
    echo -e "${RED}?${NC} Failed to create database"
    exit 1
fi

echo -e "${YELLOW}?${NC} Running initialization script..."
echo ""

# Run initialization script
"${SCRIPT_DIR}/init-database.sh" localhost

echo -e "${GREEN}???????????????????????????????????????????????????????????${NC}"
echo -e "${GREEN}?  Database reset complete!${NC}"
echo -e "${GREEN}???????????????????????????????????????????????????????????${NC}"
