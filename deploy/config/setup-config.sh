#!/bin/bash
set -e

echo "Setting up Hartonomous configuration files..."

# Create config directory in deployment paths
sudo mkdir -p /srv/www/hartonomous/api
sudo mkdir -p /srv/www/hartonomous/worker

# Copy appsettings to both API and Worker
echo "Copying appsettings.Production.json..."
sudo cp appsettings.Production.json /srv/www/hartonomous/api/
sudo cp appsettings.Production.json /srv/www/hartonomous/worker/

# Create .env file from template if it doesn't exist
if [ ! -f /srv/www/hartonomous/.env ]; then
    echo "Creating .env file from template..."
    sudo cp secrets-template.env /srv/www/hartonomous/.env
    sudo chmod 600 /srv/www/hartonomous/.env
    echo "⚠️  WARNING: Edit /srv/www/hartonomous/.env with real secrets!"
else
    echo "✓ .env file already exists, skipping..."
fi

# Set ownership
sudo chown -R www-data:www-data /srv/www/hartonomous

echo ""
echo "✓ Configuration files installed"
echo ""
echo "IMPORTANT NEXT STEPS:"
echo "1. Edit secrets: sudo nano /srv/www/hartonomous/.env"
echo "2. Generate strong passwords for:"
echo "   - PostgreSQL database user"
echo "   - JWT secret key (32+ chars)"
echo "   - Data protection key"
echo "3. Update appsettings if needed:"
echo "   - /srv/www/hartonomous/api/appsettings.Production.json"
echo "   - /srv/www/hartonomous/worker/appsettings.Production.json"
echo ""
echo "To generate random passwords:"
echo "  openssl rand -base64 32"
