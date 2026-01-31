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

echo "Setup complete. You can now run the ingestor."
