#!/bin/bash
#
# Idempotent Let's Encrypt SSL certificate setup for Hartonomous
# Installs certbot and configures certificates for nginx
# Safe to run multiple times
#

set -e

ENVIRONMENT="${1:-production}"

echo "========================================"
echo "Hartonomous Let's Encrypt Setup"
echo "Environment: $ENVIRONMENT"
echo "========================================"

# Configuration based on environment
case "$ENVIRONMENT" in
    production)
        API_DOMAIN="api.hartonomous.com"
        WEB_DOMAIN="hartonomous.com"
        WWW_DOMAIN="www.hartonomous.com"
        EMAIL="admin@hartonomous.com"
        ;;
    staging)
        API_DOMAIN="api-staging.hartonomous.com"
        WEB_DOMAIN="staging.hartonomous.com"
        WWW_DOMAIN="www-staging.hartonomous.com"
        EMAIL="admin@hartonomous.com"
        ;;
    development)
        API_DOMAIN="api-dev.hartonomous.com"
        WEB_DOMAIN="dev.hartonomous.com"
        WWW_DOMAIN="www-dev.hartonomous.com"
        EMAIL="admin@hartonomous.com"
        ;;
    *)
        echo "Error: Invalid environment. Use production, staging, or development"
        exit 1
        ;;
esac

# Ensure running as root
if [ "$EUID" -ne 0 ]; then
    echo "Error: This script must be run as root (use sudo)"
    exit 1
fi

# Check if nginx is installed
if ! command -v nginx &> /dev/null; then
    echo "Error: nginx is not installed. Run setup-nginx-config.sh first."
    exit 1
fi

# Install certbot if not already installed
if ! command -v certbot &> /dev/null; then
    echo "Installing certbot..."
    apt-get update
    apt-get install -y certbot python3-certbot-nginx
    echo "✓ certbot installed"
else
    echo "✓ certbot already installed"
fi

# Function to obtain or renew certificate
obtain_certificate() {
    local domains="$1"
    local cert_name="$2"
    
    echo "Processing certificate for: $domains"
    
    # Check if certificate already exists
    if certbot certificates 2>/dev/null | grep -q "Certificate Name: $cert_name"; then
        echo "  Certificate exists, checking if renewal is needed..."
        
        # Check expiry (renew if less than 30 days)
        if certbot renew --cert-name "$cert_name" --dry-run 2>/dev/null; then
            echo "  Certificate is valid, attempting renewal..."
            certbot renew --cert-name "$cert_name" --nginx --non-interactive --quiet || echo "  No renewal needed yet"
            echo "✓ Certificate up to date: $cert_name"
        fi
    else
        echo "  Obtaining new certificate..."
        
        # Use staging environment for non-production to avoid rate limits during testing
        if [ "$ENVIRONMENT" != "production" ]; then
            STAGING_FLAG="--staging"
        else
            STAGING_FLAG=""
        fi
        
        # Obtain certificate
        certbot certonly \
            --nginx \
            --non-interactive \
            --agree-tos \
            --email "$EMAIL" \
            --cert-name "$cert_name" \
            $STAGING_FLAG \
            -d $domains
        
        if [ $? -eq 0 ]; then
            echo "✓ Certificate obtained: $cert_name"
        else
            echo "✗ Failed to obtain certificate: $cert_name"
            return 1
        fi
    fi
}

# Obtain certificates
echo ""
echo "Obtaining SSL certificates..."
echo ""

# API certificate
echo "API Certificate:"
obtain_certificate "$API_DOMAIN" "hartonomous-api-$ENVIRONMENT"

echo ""

# Web certificate (with www subdomain)
echo "Web Certificate:"
obtain_certificate "$WEB_DOMAIN,$WWW_DOMAIN" "hartonomous-web-$ENVIRONMENT"

# Update nginx configuration to use Let's Encrypt certificates
echo ""
echo "Updating nginx configuration with Let's Encrypt certificates..."

API_SITE_CONF="/etc/nginx/sites-available/hartonomous-api-${ENVIRONMENT}"
WEB_SITE_CONF="/etc/nginx/sites-available/hartonomous-web-${ENVIRONMENT}"

# Update API site configuration
if [ -f "$API_SITE_CONF" ]; then
    echo "Updating API site configuration..."
    
    # Comment out self-signed certificate lines and uncomment Let's Encrypt lines
    sed -i 's|^\s*ssl_certificate /etc/ssl/certs/ssl-cert-snakeoil.pem;|    # ssl_certificate /etc/ssl/certs/ssl-cert-snakeoil.pem;|' "$API_SITE_CONF"
    sed -i 's|^\s*ssl_certificate_key /etc/ssl/private/ssl-cert-snakeoil.key;|    # ssl_certificate_key /etc/ssl/private/ssl-cert-snakeoil.key;|' "$API_SITE_CONF"
    sed -i "s|^\s*# ssl_certificate /etc/letsencrypt/.*|    ssl_certificate /etc/letsencrypt/live/hartonomous-api-${ENVIRONMENT}/fullchain.pem;|" "$API_SITE_CONF"
    sed -i "s|^\s*# ssl_certificate_key /etc/letsencrypt/.*|    ssl_certificate_key /etc/letsencrypt/live/hartonomous-api-${ENVIRONMENT}/privkey.pem;|" "$API_SITE_CONF"
    
    # If Let's Encrypt lines don't exist, add them
    if ! grep -q "ssl_certificate /etc/letsencrypt/" "$API_SITE_CONF"; then
        sed -i "/ssl_certificate_key.*snakeoil/a\    ssl_certificate /etc/letsencrypt/live/hartonomous-api-${ENVIRONMENT}/fullchain.pem;\n    ssl_certificate_key /etc/letsencrypt/live/hartonomous-api-${ENVIRONMENT}/privkey.pem;" "$API_SITE_CONF"
    fi
    
    echo "✓ API configuration updated"
fi

# Update Web site configuration
if [ -f "$WEB_SITE_CONF" ]; then
    echo "Updating Web site configuration..."
    
    # Comment out self-signed certificate lines and uncomment Let's Encrypt lines
    sed -i 's|^\s*ssl_certificate /etc/ssl/certs/ssl-cert-snakeoil.pem;|    # ssl_certificate /etc/ssl/certs/ssl-cert-snakeoil.pem;|' "$WEB_SITE_CONF"
    sed -i 's|^\s*ssl_certificate_key /etc/ssl/private/ssl-cert-snakeoil.key;|    # ssl_certificate_key /etc/ssl/private/ssl-cert-snakeoil.key;|' "$WEB_SITE_CONF"
    sed -i "s|^\s*# ssl_certificate /etc/letsencrypt/.*|    ssl_certificate /etc/letsencrypt/live/hartonomous-web-${ENVIRONMENT}/fullchain.pem;|" "$WEB_SITE_CONF"
    sed -i "s|^\s*# ssl_certificate_key /etc/letsencrypt/.*|    ssl_certificate_key /etc/letsencrypt/live/hartonomous-web-${ENVIRONMENT}/privkey.pem;|" "$WEB_SITE_CONF"
    
    # If Let's Encrypt lines don't exist, add them
    if ! grep -q "ssl_certificate /etc/letsencrypt/" "$WEB_SITE_CONF"; then
        sed -i "/ssl_certificate_key.*snakeoil/a\    ssl_certificate /etc/letsencrypt/live/hartonomous-web-${ENVIRONMENT}/fullchain.pem;\n    ssl_certificate_key /etc/letsencrypt/live/hartonomous-web-${ENVIRONMENT}/privkey.pem;" "$WEB_SITE_CONF"
    fi
    
    echo "✓ Web configuration updated"
fi

# Test nginx configuration
echo ""
echo "Testing nginx configuration..."
if nginx -t; then
    echo "✓ nginx configuration is valid"
    
    # Reload nginx
    echo "Reloading nginx..."
    systemctl reload nginx
    echo "✓ nginx reloaded with Let's Encrypt certificates"
else
    echo "✗ nginx configuration test failed"
    exit 1
fi

# Setup automatic renewal (idempotent)
echo ""
echo "Configuring automatic certificate renewal..."

# certbot installs a systemd timer by default, verify it's enabled
if systemctl is-enabled certbot.timer &>/dev/null; then
    echo "✓ Automatic renewal already configured (certbot.timer)"
else
    systemctl enable certbot.timer
    systemctl start certbot.timer
    echo "✓ Automatic renewal enabled (certbot.timer)"
fi

# Add post-renewal hook to reload nginx (idempotent)
RENEWAL_HOOK_DIR="/etc/letsencrypt/renewal-hooks/post"
RENEWAL_HOOK="$RENEWAL_HOOK_DIR/reload-nginx.sh"

if [ ! -f "$RENEWAL_HOOK" ]; then
    echo "Creating renewal hook to reload nginx..."
    mkdir -p "$RENEWAL_HOOK_DIR"
    
    cat > "$RENEWAL_HOOK" <<'EOF'
#!/bin/bash
systemctl reload nginx
EOF
    
    chmod +x "$RENEWAL_HOOK"
    echo "✓ Renewal hook created"
else
    echo "✓ Renewal hook already exists"
fi

# Summary
echo ""
echo "========================================"
echo "Let's Encrypt Setup Complete!"
echo "========================================"
echo ""
echo "Certificates installed:"
echo "  API: $API_DOMAIN"
echo "  Web: $WEB_DOMAIN, $WWW_DOMAIN"
echo ""
echo "Certificate locations:"
echo "  API: /etc/letsencrypt/live/hartonomous-api-${ENVIRONMENT}/"
echo "  Web: /etc/letsencrypt/live/hartonomous-web-${ENVIRONMENT}/"
echo ""
echo "Automatic renewal:"
echo "  Status: $(systemctl is-active certbot.timer)"
echo "  Next run: $(systemctl status certbot.timer | grep -oP 'Trigger: \K.*' || echo 'Check with: systemctl status certbot.timer')"
echo ""
echo "Manual renewal test:"
echo "  certbot renew --dry-run"
echo ""
echo "✓ SSL/TLS certificates active"
