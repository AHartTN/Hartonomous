#!/bin/bash
set -e

echo "Setting up PostgreSQL for Hartonomous..."

# Check if PostgreSQL is running
if ! systemctl is-active --quiet postgresql; then
    echo "Starting PostgreSQL..."
    sudo systemctl start postgresql
fi

# Prompt for password
echo ""
echo "You will be prompted to set a password for the hartonomous_user database user."
read -sp "Enter password for hartonomous_user: " DB_PASSWORD
echo ""
read -sp "Confirm password: " DB_PASSWORD_CONFIRM
echo ""

if [ "$DB_PASSWORD" != "$DB_PASSWORD_CONFIRM" ]; then
    echo "❌ Passwords do not match!"
    exit 1
fi

# Create temporary SQL file with password replaced
TMP_SQL=$(mktemp)
sed "s/CHANGE_ME_STRONG_PASSWORD/$DB_PASSWORD/g" init-postgresql.sql > "$TMP_SQL"

# Run initialization script
echo "Creating database and tables..."
sudo -u postgres psql -f "$TMP_SQL"

# Clean up temp file
rm "$TMP_SQL"

echo ""
echo "✓ PostgreSQL database initialized successfully!"
echo ""
echo "Connection details:"
echo "  Database: hartonomous"
echo "  Host: localhost"
echo "  Port: 5432"
echo "  User: hartonomous_user"
echo "  Password: [the password you just entered]"
echo ""
echo "Connection string:"
echo "  Host=localhost;Port=5432;Database=hartonomous;Username=hartonomous_user;Password=YOUR_PASSWORD"
echo ""
echo "To connect:"
echo "  psql -h localhost -U hartonomous_user -d hartonomous"
echo ""
echo "IMPORTANT: Update /srv/www/hartonomous/.env with this connection string!"
