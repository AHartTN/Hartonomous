#!/bin/bash
#
# Idempotent nginx reverse proxy setup for Hartonomous
# Configures nginx with SSL/TLS, rate limiting, and proper upstream configuration
# Safe to run multiple times
#

set -e

ENVIRONMENT="${1:-production}"

echo "========================================"
echo "Hartonomous nginx Configuration Setup"
echo "Environment: $ENVIRONMENT"
echo "========================================"

# Configuration based on environment
case "$ENVIRONMENT" in
    production)
        API_PORT=5000
        WEB_PORT=5001
        API_DOMAIN="api.hartonomous.com"
        WEB_DOMAIN="hartonomous.com"
        WWW_DOMAIN="www.hartonomous.com"
        ;;
    staging)
        API_PORT=5010
        WEB_PORT=5011
        API_DOMAIN="api-staging.hartonomous.com"
        WEB_DOMAIN="staging.hartonomous.com"
        WWW_DOMAIN="www-staging.hartonomous.com"
        ;;
    development)
        API_PORT=5020
        WEB_PORT=5021
        API_DOMAIN="api-dev.hartonomous.com"
        WEB_DOMAIN="dev.hartonomous.com"
        WWW_DOMAIN="www-dev.hartonomous.com"
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

# Ensure nginx is installed
if ! command -v nginx &> /dev/null; then
    echo "Installing nginx..."
    apt-get update
    apt-get install -y nginx
    echo "✓ nginx installed"
else
    echo "✓ nginx already installed"
fi

# Create directories
ensure_directory() {
    local dir="$1"
    if [ ! -d "$dir" ]; then
        echo "Creating directory: $dir"
        mkdir -p "$dir"
        echo "✓ Created directory"
    else
        echo "✓ Directory exists: $dir"
    fi
}

ensure_directory "/etc/nginx/sites-available"
ensure_directory "/etc/nginx/sites-enabled"
ensure_directory "/var/log/nginx/hartonomous"
ensure_directory "/var/cache/nginx/hartonomous"

# Create rate limiting zones configuration
echo ""
echo "Configuring rate limiting..."
RATE_LIMIT_CONF="/etc/nginx/conf.d/hartonomous-rate-limits.conf"

cat > "$RATE_LIMIT_CONF" <<'EOF'
# Rate limiting zones for Hartonomous
# Limit requests by IP address

# API rate limiting - 100 requests per minute per IP
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=100r/m;

# Web rate limiting - 200 requests per minute per IP
limit_req_zone $binary_remote_addr zone=web_limit:10m rate=200r/m;

# Connection limiting - 10 concurrent connections per IP
limit_conn_zone $binary_remote_addr zone=conn_limit:10m;
EOF

echo "✓ Rate limiting configured: $RATE_LIMIT_CONF"

# Create upstream configuration
echo ""
echo "Configuring upstream servers..."
UPSTREAM_CONF="/etc/nginx/conf.d/hartonomous-upstream-${ENVIRONMENT}.conf"

cat > "$UPSTREAM_CONF" <<EOF
# Upstream servers for Hartonomous ($ENVIRONMENT)

upstream hartonomous_api_${ENVIRONMENT} {
    server localhost:${API_PORT} fail_timeout=5s max_fails=3;
    keepalive 32;
}

upstream hartonomous_web_${ENVIRONMENT} {
    server localhost:${WEB_PORT} fail_timeout=5s max_fails=3;
    keepalive 32;
}
EOF

echo "✓ Upstream configuration: $UPSTREAM_CONF"

# Create API site configuration
echo ""
echo "Configuring API site..."
API_SITE_CONF="/etc/nginx/sites-available/hartonomous-api-${ENVIRONMENT}"

cat > "$API_SITE_CONF" <<EOF
# Hartonomous API ($ENVIRONMENT)
# $API_DOMAIN

server {
    listen 80;
    listen [::]:80;
    server_name $API_DOMAIN;
    
    # Redirect HTTP to HTTPS
    return 301 https://\$server_name\$request_uri;
}

server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name $API_DOMAIN;
    
    # SSL Configuration
    # Note: Replace with actual certificate paths
    # ssl_certificate /etc/letsencrypt/live/$API_DOMAIN/fullchain.pem;
    # ssl_certificate_key /etc/letsencrypt/live/$API_DOMAIN/privkey.pem;
    ssl_certificate /etc/ssl/certs/ssl-cert-snakeoil.pem;
    ssl_certificate_key /etc/ssl/private/ssl-cert-snakeoil.key;
    
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;
    
    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "DENY" always;
    add_header X-XSS-Protection "1; mode=block" always;
    
    # Logging
    access_log /var/log/nginx/hartonomous/api-${ENVIRONMENT}-access.log;
    error_log /var/log/nginx/hartonomous/api-${ENVIRONMENT}-error.log;
    
    # Rate limiting
    limit_req zone=api_limit burst=20 nodelay;
    limit_conn conn_limit 10;
    
    # Client body size
    client_max_body_size 10M;
    
    # Proxy settings
    location / {
        proxy_pass http://hartonomous_api_${ENVIRONMENT};
        proxy_http_version 1.1;
        
        # Headers
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_set_header X-Forwarded-Host \$host;
        proxy_set_header X-Forwarded-Port \$server_port;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
        
        # Buffering
        proxy_buffering on;
        proxy_buffer_size 4k;
        proxy_buffers 8 4k;
        proxy_busy_buffers_size 8k;
        
        # Cache bypass
        proxy_cache_bypass \$http_upgrade;
    }
    
    # Health check endpoint (no rate limiting)
    location /health {
        proxy_pass http://hartonomous_api_${ENVIRONMENT}/health;
        access_log off;
        limit_req off;
        limit_conn off;
    }
}
EOF

echo "✓ API site configuration: $API_SITE_CONF"

# Create Web site configuration
echo ""
echo "Configuring Web site..."
WEB_SITE_CONF="/etc/nginx/sites-available/hartonomous-web-${ENVIRONMENT}"

cat > "$WEB_SITE_CONF" <<EOF
# Hartonomous Web ($ENVIRONMENT)
# $WEB_DOMAIN, $WWW_DOMAIN

server {
    listen 80;
    listen [::]:80;
    server_name $WEB_DOMAIN $WWW_DOMAIN;
    
    # Redirect HTTP to HTTPS
    return 301 https://\$server_name\$request_uri;
}

server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name $WEB_DOMAIN $WWW_DOMAIN;
    
    # SSL Configuration
    # Note: Replace with actual certificate paths
    # ssl_certificate /etc/letsencrypt/live/$WEB_DOMAIN/fullchain.pem;
    # ssl_certificate_key /etc/letsencrypt/live/$WEB_DOMAIN/privkey.pem;
    ssl_certificate /etc/ssl/certs/ssl-cert-snakeoil.pem;
    ssl_certificate_key /etc/ssl/private/ssl-cert-snakeoil.key;
    
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;
    
    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-XSS-Protection "1; mode=block" always;
    
    # Logging
    access_log /var/log/nginx/hartonomous/web-${ENVIRONMENT}-access.log;
    error_log /var/log/nginx/hartonomous/web-${ENVIRONMENT}-error.log;
    
    # Rate limiting
    limit_req zone=web_limit burst=50 nodelay;
    limit_conn conn_limit 20;
    
    # Client body size
    client_max_body_size 10M;
    
    # Gzip compression
    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript application/wasm;
    gzip_vary on;
    gzip_comp_level 6;
    
    # Static files caching
    location ~* \.(jpg|jpeg|png|gif|ico|css|js|svg|woff|woff2|ttf|eot)$ {
        proxy_pass http://hartonomous_web_${ENVIRONMENT};
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
    
    # Proxy settings
    location / {
        proxy_pass http://hartonomous_web_${ENVIRONMENT};
        proxy_http_version 1.1;
        
        # WebSocket support
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        
        # Headers
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_set_header X-Forwarded-Host \$host;
        proxy_set_header X-Forwarded-Port \$server_port;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
        
        # Buffering
        proxy_buffering on;
        proxy_buffer_size 4k;
        proxy_buffers 8 4k;
        proxy_busy_buffers_size 8k;
        
        # Cache bypass
        proxy_cache_bypass \$http_upgrade;
    }
    
    # Health check endpoint (no rate limiting)
    location /health {
        proxy_pass http://hartonomous_web_${ENVIRONMENT}/health;
        access_log off;
        limit_req off;
        limit_conn off;
    }
}
EOF

echo "✓ Web site configuration: $WEB_SITE_CONF"

# Enable sites (idempotent)
echo ""
echo "Enabling sites..."

for site in "hartonomous-api-${ENVIRONMENT}" "hartonomous-web-${ENVIRONMENT}"; do
    if [ ! -L "/etc/nginx/sites-enabled/$site" ]; then
        echo "Enabling site: $site"
        ln -s "/etc/nginx/sites-available/$site" "/etc/nginx/sites-enabled/$site"
        echo "✓ Site enabled"
    else
        echo "✓ Site already enabled: $site"
    fi
done

# Test nginx configuration
echo ""
echo "Testing nginx configuration..."
if nginx -t; then
    echo "✓ nginx configuration is valid"
    
    # Reload nginx
    echo "Reloading nginx..."
    systemctl reload nginx
    echo "✓ nginx reloaded"
else
    echo "✗ nginx configuration test failed"
    exit 1
fi

# Summary
echo ""
echo "========================================"
echo "nginx Configuration Complete!"
echo "========================================"
echo ""
echo "API Configuration:"
echo "  Domain: $API_DOMAIN"
echo "  Upstream: localhost:$API_PORT"
echo "  Rate Limit: 100 req/min per IP"
echo ""
echo "Web Configuration:"
echo "  Domains: $WEB_DOMAIN, $WWW_DOMAIN"
echo "  Upstream: localhost:$WEB_PORT"
echo "  Rate Limit: 200 req/min per IP"
echo ""
echo "⚠ SSL Certificates:"
echo "  Currently using self-signed certificates"
echo "  Replace with Let's Encrypt certificates:"
echo "    sudo certbot --nginx -d $API_DOMAIN"
echo "    sudo certbot --nginx -d $WEB_DOMAIN -d $WWW_DOMAIN"
echo ""
echo "Configuration files:"
echo "  /etc/nginx/sites-available/hartonomous-api-${ENVIRONMENT}"
echo "  /etc/nginx/sites-available/hartonomous-web-${ENVIRONMENT}"
echo ""
echo "✓ Ready for deployment"
