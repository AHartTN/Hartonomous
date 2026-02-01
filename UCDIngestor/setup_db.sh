#!/bin/bash
set -e

DB_NAME=${PGDATABASE:-ucd}

echo "Setting up database: $DB_NAME"

# Create database if it doesn't exist
if ! psql -lqt | cut -d \| -f 1 | grep -qw $DB_NAME; then
    createdb $DB_NAME
    echo "Database '$DB_NAME' created."
else
    echo "Database '$DB_NAME' already exists."
fi

# Apply the Gene Pool Schema
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
psql -d $DB_NAME -f "$SCRIPT_DIR/ucd_gene_pool.sql"

echo "Setup complete. You can now run the ingestor."
