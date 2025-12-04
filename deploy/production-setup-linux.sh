#!/bin/bash
#############################################################################
# Hartonomous Production Deployment Script for Linux (Ubuntu/Debian)
# Target: HART-SERVER
# Environment: Production/Staging/Development
# Author: Anthony Hart
# Date: 2025-12-04
#############################################################################

set -e  # Exit on error
set -u  # Exit on undefined variable

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration - Environment based
ENVIRONMENT="${1:-production}"

case $ENVIRONMENT in
    production)
        DEPLOY_DIR="/srv/www/production"
        NGINX_SERVER_NAME="api.hartonomous.com app.hartonomous.com"
        ;;
    staging)
        DEPLOY_DIR="/srv/www/staging"
        NGINX_SERVER_NAME="api-staging.hartonomous.com app-staging.hartonomous.com"
        ;;
    development|dev)
        DEPLOY_DIR="/srv/www/development"
        NGINX_SERVER_NAME="api-dev.hartonomous.com app-dev.hartonomous.com"
        ;;
    *)
        echo -e "${RED}Invalid environment: $ENVIRONMENT${NC}"
        echo "Usage: $0 [production|staging|development]"
        exit 1
        ;;
esac

APP_NAME="hartonomous"
APP_USER="www-data"
APP_GROUP="www-data"
LOG_DIR="/var/log/hartonomous/$ENVIRONMENT"
DATA_DIR="/var/lib/hartonomous/$ENVIRONMENT"
SYSTEMD_DIR="/etc/systemd/system"
NGINX_DIR="/etc/nginx/sites-available"
NGINX_ENABLED="/etc/nginx/sites-enabled"

# Dotnet version
DOTNET_VERSION="10.0"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Hartonomous ${ENVIRONMENT^} Deployment${NC}"
echo -e "${GREEN}========================================${NC}"

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo -e "${RED}Please run as root (sudo)${NC}"
    exit 1
fi

echo -e "${YELLOW}Step 1: Installing system dependencies...${NC}"

# Update system
apt-get update
apt-get upgrade -y

# Install required packages
apt-get install -y \
    apt-transport-https \
    ca-certificates \
    curl \
    gnupg \
    lsb-release \
    software-properties-common \
    nginx \
    postgresql-client \
    redis-tools \
    git \
    wget \
    unzip

echo -e "${GREEN}? System dependencies installed${NC}"

echo -e "${YELLOW}Step 2: Installing .NET 10 Runtime...${NC}"

# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET 10 Runtime
apt-get update
apt-get install -y aspnetcore-runtime-${DOTNET_VERSION}
apt-get install -y dotnet-runtime-${DOTNET_VERSION}

# Verify installation
dotnet --version

echo -e "${GREEN}? .NET ${DOTNET_VERSION} Runtime installed${NC}"

echo -e "${YELLOW}Step 3: Creating deployment directories...${NC}"

# Create /srv/www structure if it doesn't exist
mkdir -p /srv/www

# Create environment-specific directories
mkdir -p "$DEPLOY_DIR"/{api,worker,web}
mkdir -p "$LOG_DIR"
mkdir -p "$DATA_DIR"

# Set permissions (using www-data which is standard for web servers)
chown -R "$APP_USER":"$APP_GROUP" "$DEPLOY_DIR"
chown -R "$APP_USER":"$APP_GROUP" "$LOG_DIR"
chown -R "$APP_USER":"$APP_GROUP" "$DATA_DIR"

chmod 755 "$DEPLOY_DIR"
chmod 755 "$LOG_DIR"
chmod 700 "$DATA_DIR"  # Data directory should be more restricted

echo -e "${GREEN}? Directories created at $DEPLOY_DIR${NC}"

echo -e "${YELLOW}Step 4: Configuring systemd services...${NC}"

# Create systemd service for API
cat > "$SYSTEMD_DIR/hartonomous-api-$ENVIRONMENT.service" << EOF
[Unit]
Description=Hartonomous API Service ($ENVIRONMENT)
After=network.target postgresql.service redis.service
Wants=postgresql.service redis.service

[Service]
Type=notify
User=$APP_USER
Group=$APP_GROUP
WorkingDirectory=$DEPLOY_DIR/api
ExecStart=/usr/bin/dotnet $DEPLOY_DIR/api/Hartonomous.API.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=hartonomous-api-$ENVIRONMENT
Environment=ASPNETCORE_ENVIRONMENT=${ENVIRONMENT^}
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=ASPNETCORE_URLS=http://localhost:$([ "$ENVIRONMENT" = "production" ] && echo "5000" || [ "$ENVIRONMENT" = "staging" ] && echo "5001" || echo "5002")

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$DEPLOY_DIR/api $LOG_DIR $DATA_DIR
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictRealtime=true
RestrictNamespaces=true
LockPersonality=true
MemoryDenyWriteExecute=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
SystemCallArchitectures=native

# Resource limits
LimitNOFILE=65536
LimitNPROC=4096

[Install]
WantedBy=multi-user.target
EOF

# Create systemd service for Worker
cat > "$SYSTEMD_DIR/hartonomous-worker-$ENVIRONMENT.service" << EOF
[Unit]
Description=Hartonomous Worker Service ($ENVIRONMENT)
After=network.target postgresql.service redis.service
Wants=postgresql.service redis.service

[Service]
Type=notify
User=$APP_USER
Group=$APP_GROUP
WorkingDirectory=$DEPLOY_DIR/worker
ExecStart=/usr/bin/dotnet $DEPLOY_DIR/worker/Hartonomous.Worker.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=hartonomous-worker-$ENVIRONMENT
Environment=DOTNET_ENVIRONMENT=${ENVIRONMENT^}
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$DEPLOY_DIR/worker $LOG_DIR $DATA_DIR
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictRealtime=true
RestrictNamespaces=true
LockPersonality=true
MemoryDenyWriteExecute=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
SystemCallArchitectures=native

# Resource limits
LimitNOFILE=65536
LimitNPROC=4096

[Install]
WantedBy=multi-user.target
EOF

# Reload systemd
systemctl daemon-reload

echo -e "${GREEN}? Systemd services configured for $ENVIRONMENT${NC}"

echo -e "${YELLOW}Step 5: Configuring Nginx reverse proxy...${NC}"

# Determine port based on environment
if [ "$ENVIRONMENT" = "production" ]; then
    API_PORT=5000
elif [ "$ENVIRONMENT" = "staging" ]; then
    API_PORT=5001
else
    API_PORT=5002
fi

# Create Nginx configuration
cat > "$NGINX_DIR/hartonomous-$ENVIRONMENT" << EOF
# Rate limiting zones for $ENVIRONMENT
limit_req_zone \$binary_remote_addr zone=${ENVIRONMENT}_api_limit:10m rate=10r/s;
limit_req_zone \$binary_remote_addr zone=${ENVIRONMENT}_general_limit:10m rate=100r/s;
limit_conn_zone \$binary_remote_addr zone=${ENVIRONMENT}_conn_limit:10m;

# Upstream server for $ENVIRONMENT
upstream hartonomous_api_$ENVIRONMENT {
    server localhost:$API_PORT fail_timeout=0;
}

# Redirect HTTP to HTTPS
server {
    listen 80;
    listen [::]:80;
    server_name $NGINX_SERVER_NAME;
    return 301 https://\$server_name\$request_uri;
}

# API Server - $ENVIRONMENT
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name $(echo $NGINX_SERVER_NAME | awk '{print $1}');

    # SSL Configuration (update paths after obtaining certificates)
    ssl_certificate /etc/letsencrypt/live/$(echo $NGINX_SERVER_NAME | awk '{print $1}')/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/$(echo $NGINX_SERVER_NAME | awk '{print $1}')/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;

    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    # Logging
    access_log /var/log/nginx/hartonomous-${ENVIRONMENT}-api-access.log;
    error_log /var/log/nginx/hartonomous-${ENVIRONMENT}-api-error.log;

    # Rate limiting
    limit_req zone=${ENVIRONMENT}_api_limit burst=20 nodelay;
    limit_conn ${ENVIRONMENT}_conn_limit 10;

    # Client body size
    client_max_body_size 10M;

    # Root directory for static files if needed
    root $DEPLOY_DIR/web/wwwroot;

    location / {
        proxy_pass http://hartonomous_api_$ENVIRONMENT;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
        proxy_buffering off;
        proxy_read_timeout 300s;
        proxy_connect_timeout 75s;
    }

    # Health check endpoint (no rate limiting)
    location /health {
        proxy_pass http://hartonomous_api_$ENVIRONMENT/health;
        access_log off;
    }

    # Metrics endpoint (restrict access)
    location /metrics {
        allow 10.0.0.0/8;
        allow 172.16.0.0/12;
        allow 192.168.0.0/16;
        allow 127.0.0.1;
        deny all;
        proxy_pass http://hartonomous_api_$ENVIRONMENT/metrics;
    }
}

# Web App Server - $ENVIRONMENT
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name $(echo $NGINX_SERVER_NAME | awk '{print $2}');

    # SSL Configuration
    ssl_certificate /etc/letsencrypt/live/$(echo $NGINX_SERVER_NAME | awk '{print $2}')/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/$(echo $NGINX_SERVER_NAME | awk '{print $2}')/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;

    # Logging
    access_log /var/log/nginx/hartonomous-${ENVIRONMENT}-web-access.log;
    error_log /var/log/nginx/hartonomous-${ENVIRONMENT}-web-error.log;

    # Root directory
    root $DEPLOY_DIR/web/wwwroot;
    index index.html;

    location / {
        try_files \$uri \$uri/ /index.html;
    }

    # Static assets caching
    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
}
EOF

# Test Nginx configuration
nginx -t

# Enable site
ln -sf "$NGINX_DIR/hartonomous-$ENVIRONMENT" "$NGINX_ENABLED/hartonomous-$ENVIRONMENT"

echo -e "${GREEN}? Nginx configured for $ENVIRONMENT${NC}"

echo -e "${YELLOW}Step 6: Configuring firewall (UFW)...${NC}"

# Install and configure UFW if not already present
apt-get install -y ufw

# Default policies
ufw default deny incoming
ufw default allow outgoing

# Allow SSH (IMPORTANT!)
ufw allow ssh

# Allow HTTP and HTTPS
ufw allow 80/tcp
ufw allow 443/tcp

# Enable firewall (with --force to avoid confirmation prompt)
ufw --force enable

echo -e "${GREEN}? Firewall configured${NC}"

echo -e "${YELLOW}Step 7: Setting up log rotation...${NC}"

# Create logrotate configuration
cat > /etc/logrotate.d/hartonomous-$ENVIRONMENT << EOF
$LOG_DIR/*.log {
    daily
    rotate 14
    compress
    delaycompress
    notifempty
    create 0644 $APP_USER $APP_GROUP
    sharedscripts
    postrotate
        systemctl reload hartonomous-api-$ENVIRONMENT hartonomous-worker-$ENVIRONMENT || true
    endscript
}
EOF

echo -e "${GREEN}? Log rotation configured${NC}"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}$ENVIRONMENT Environment Setup Complete!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo -e "${YELLOW}Deployment directory: $DEPLOY_DIR${NC}"
echo "  - API: $DEPLOY_DIR/api"
echo "  - Worker: $DEPLOY_DIR/worker"
echo "  - Web: $DEPLOY_DIR/web"
echo ""
echo -e "${YELLOW}Logs directory: $LOG_DIR${NC}"
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Copy your application files to $DEPLOY_DIR"
echo ""
echo "2. Obtain SSL certificates:"
echo "   sudo certbot --nginx -d $(echo $NGINX_SERVER_NAME | sed 's/ / -d /g')"
echo ""
echo "3. Start services:"
echo "   sudo systemctl start hartonomous-api-$ENVIRONMENT"
echo "   sudo systemctl start hartonomous-worker-$ENVIRONMENT"
echo "   sudo systemctl restart nginx"
echo ""
echo "4. Enable services to start on boot:"
echo "   sudo systemctl enable hartonomous-api-$ENVIRONMENT"
echo "   sudo systemctl enable hartonomous-worker-$ENVIRONMENT"
echo ""
echo "5. Check service status:"
echo "   sudo systemctl status hartonomous-api-$ENVIRONMENT"
echo ""
echo "6. View logs:"
echo "   sudo journalctl -u hartonomous-api-$ENVIRONMENT -f"
echo ""
echo -e "${GREEN}Server is ready for $ENVIRONMENT deployment!${NC}"
