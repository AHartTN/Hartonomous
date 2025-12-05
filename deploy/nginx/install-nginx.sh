#!/bin/bash
set -e

echo "Installing Nginx and configuring Hartonomous API..."

# Install Nginx if not already installed
if ! command -v nginx &> /dev/null; then
    echo "Installing Nginx..."
    sudo apt update
    sudo apt install -y nginx
fi

# Create certbot directory for Let's Encrypt validation
sudo mkdir -p /var/www/certbot

# Copy configuration
echo "Installing Nginx configuration..."
sudo cp hartonomous-api.conf /etc/nginx/sites-available/

# Enable site
sudo ln -sf /etc/nginx/sites-available/hartonomous-api.conf /etc/nginx/sites-enabled/

# Test configuration
echo "Testing Nginx configuration..."
sudo nginx -t

# Reload Nginx
echo "Reloading Nginx..."
sudo systemctl reload nginx

echo ""
echo "✓ Nginx configured successfully"
echo ""
echo "Next steps:"
echo "1. Update DNS to point api.hartonomous.local to this server"
echo "2. Obtain SSL certificate:"
echo "   sudo certbot --nginx -d api.hartonomous.local"
echo ""
echo "Or use certbot DNS challenge for internal domains:"
echo "   sudo certbot certonly --manual --preferred-challenges dns -d api.hartonomous.local"
echo ""
echo "To check Nginx status:"
echo "   sudo systemctl status nginx"
echo ""
echo "To view Nginx logs:"
echo "   sudo tail -f /var/log/nginx/hartonomous-api-*.log"
