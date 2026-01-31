#!/bin/bash
set -e
DB="${1:-hartonomous}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
dropdb --if-exists "$DB"
createdb "$DB"
psql -d "$DB" -c "CREATE EXTENSION IF NOT EXISTS postgis;"
psql -d "$DB" -c "CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";"
cd "$SCRIPT_DIR/sql"
psql -d "$DB" -f "00-init-schema.sql"
echo "Database $DB reset complete"
