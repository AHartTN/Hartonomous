#!/bin/bash
# Fix PostgreSQL authentication for local development

echo "Adding trust authentication for local connections..."

# Backup current config
sudo cp /etc/postgresql/16/main/pg_hba.conf /etc/postgresql/16/main/pg_hba.conf.backup.$(date +%Y%m%d)

# Add local trust authentication (at the top so it takes precedence)
sudo bash -c 'cat > /tmp/pg_hba_additions.conf << EOF
# Local development - trust authentication
local   hartonomous     all                                     trust
host    hartonomous     all             127.0.0.1/32            trust
host    hartonomous     all             ::1/128                 trust
EOF'

# Prepend to existing config
sudo bash -c 'cat /tmp/pg_hba_additions.conf /etc/postgresql/16/main/pg_hba.conf > /tmp/pg_hba_new.conf'
sudo mv /tmp/pg_hba_new.conf /etc/postgresql/16/main/pg_hba.conf
sudo chown postgres:postgres /etc/postgresql/16/main/pg_hba.conf
sudo chmod 640 /etc/postgresql/16/main/pg_hba.conf

# Reload PostgreSQL
sudo systemctl reload postgresql

echo "✅ PostgreSQL authentication updated"
echo "Testing connection..."

sleep 2

# Test connection
psql -h /var/run/postgresql -U ahart -d hartonomous -c "SELECT 1;" && echo "✅ Connection successful!"
