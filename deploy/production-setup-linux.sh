#!/bin/bash
#############################################################################
# Hartonomous Production Deployment Script for Linux (Ubuntu/Debian)
# Target: HART-SERVER
# Environment: Production
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

# Configuration
APP_NAME="hartonomous"
APP_USER="hartonomous"
APP_GROUP="hartonomous"
INSTALL_DIR="/opt/hartonomous"
LOG_DIR="/var/log/hartonomous"
DATA_DIR="/var/lib/hartonomous"
SYSTEMD_DIR="/etc/systemd/system"
NGINX_DIR="/etc/nginx/sites-available"
NGINX_ENABLED="/etc/nginx/sites-enabled"

# Dotnet version
DOTNET_VERSION="10.0"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Hartonomous Production Deployment${NC}"
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

echo -e "${YELLOW}Step 3: Creating application user and directories...${NC}"

# Create application user (no login shell)
if ! id -u "$APP_USER" > /dev/null 2>&1; then
    useradd -r -s /bin/false -d "$INSTALL_DIR" "$APP_USER"
    echo -e "${GREEN}? Created user: $APP_USER${NC}"
else
    echo -e "${YELLOW}User $APP_USER already exists${NC}"
fi

# Create directories
mkdir -p "$INSTALL_DIR"/{api,worker,web}
mkdir -p "$LOG_DIR"
mkdir -p "$DATA_DIR"

# Set permissions
chown -R "$APP_USER":"$APP_GROUP" "$INSTALL_DIR"
chown -R "$APP_USER":"$APP_GROUP" "$LOG_DIR"
chown -R "$APP_USER":"$APP_GROUP" "$DATA_DIR"

chmod 755 "$INSTALL_DIR"
chmod 755 "$LOG_DIR"
chmod 700 "$DATA_DIR"  # Data directory should be more restricted

echo -e "${GREEN}? Directories created and permissions set${NC}"

echo -e "${YELLOW}Step 4: Configuring systemd services...${NC}"

# Create systemd service for API
cat > "$SYSTEMD_DIR/hartonomous-api.service" << 'EOF'
[Unit]
Description=Hartonomous API Service
After=network.target postgresql.service redis.service
Wants=postgresql.service redis.service

[Service]
Type=notify
User=hartonomous
Group=hartonomous
WorkingDirectory=/opt/hartonomous/api
ExecStart=/usr/bin/dotnet /opt/hartonomous/api/Hartonomous.API.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=hartonomous-api
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=ASPNETCORE_URLS=http://localhost:5000

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/hartonomous/api /var/log/hartonomous /var/lib/hartonomous
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
cat > "$SYSTEMD_DIR/hartonomous-worker.service" << 'EOF'
[Unit]
Description=Hartonomous Worker Service
After=network.target postgresql.service redis.service
Wants=postgresql.service redis.service

[Service]
Type=notify
User=hartonomous
Group=hartonomous
WorkingDirectory=/opt/hartonomous/worker
ExecStart=/usr/bin/dotnet /opt/hartonomous/worker/Hartonomous.Worker.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=hartonomous-worker
Environment=DOTNET_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/hartonomous/worker /var/log/hartonomous /var/lib/hartonomous
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

echo -e "${GREEN}? Systemd services configured${NC}"

echo -e "${YELLOW}Step 5: Configuring Nginx reverse proxy...${NC}"

# Create Nginx configuration
cat > "$NGINX_DIR/hartonomous" << 'EOF'
# Rate limiting zones
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=10r/s;
limit_req_zone $binary_remote_addr zone=general_limit:10m rate=100r/s;
limit_conn_zone $binary_remote_addr zone=conn_limit:10m;

# Upstream servers
upstream hartonomous_api {
    server localhost:5000 fail_timeout=0;
}

# Redirect HTTP to HTTPS
server {
    listen 80;
    listen [::]:80;
    server_name api.hartonomous.com app.hartonomous.com;
    return 301 https://$server_name$request_uri;
}

# API Server
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name api.hartonomous.com;

    # SSL Configuration (update paths after obtaining certificates)
    ssl_certificate /etc/letsencrypt/live/api.hartonomous.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.hartonomous.com/privkey.pem;
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
    access_log /var/log/nginx/hartonomous-api-access.log;
    error_log /var/log/nginx/hartonomous-api-error.log;

    # Rate limiting
    limit_req zone=api_limit burst=20 nodelay;
    limit_conn conn_limit 10;

    # Client body size
    client_max_body_size 10M;

    location / {
        proxy_pass http://hartonomous_api;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        proxy_buffering off;
        proxy_read_timeout 300s;
        proxy_connect_timeout 75s;
    }

    # Health check endpoint (no rate limiting)
    location /health {
        proxy_pass http://hartonomous_api/health;
        access_log off;
    }

    # Metrics endpoint (restrict access)
    location /metrics {
        allow 10.0.0.0/8;
        allow 172.16.0.0/12;
        allow 192.168.0.0/16;
        deny all;
        proxy_pass http://hartonomous_api/metrics;
    }
}
EOF

# Test Nginx configuration
nginx -t

# Enable site
ln -sf "$NGINX_DIR/hartonomous" "$NGINX_ENABLED/hartonomous"

echo -e "${GREEN}? Nginx configured${NC}"

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

# Allow PostgreSQL from localhost only
ufw allow from 127.0.0.1 to any port 5432

# Allow Redis from localhost only
ufw allow from 127.0.0.1 to any port 6379

# Enable firewall (with --force to avoid confirmation prompt)
ufw --force enable

echo -e "${GREEN}? Firewall configured${NC}"

echo -e "${YELLOW}Step 7: Setting up log rotation...${NC}"

# Create logrotate configuration
cat > /etc/logrotate.d/hartonomous << 'EOF'
/var/log/hartonomous/*.log {
    daily
    rotate 14
    compress
    delaycompress
    notifempty
    create 0644 hartonomous hartonomous
    sharedscripts
    postrotate
        systemctl reload hartonomous-api hartonomous-worker || true
    endscript
}
EOF

echo -e "${GREEN}? Log rotation configured${NC}"

echo -e "${YELLOW}Step 8: Installing monitoring tools...${NC}"

# Install Prometheus Node Exporter
if ! command -v node_exporter &> /dev/null; then
    wget https://github.com/prometheus/node_exporter/releases/latest/download/node_exporter-*linux-amd64.tar.gz
    tar xvfz node_exporter-*linux-amd64.tar.gz
    cp node_exporter-*/node_exporter /usr/local/bin/
    rm -rf node_exporter-*
    
    # Create systemd service
    cat > /etc/systemd/system/node_exporter.service << 'EOF'
[Unit]
Description=Node Exporter
After=network.target

[Service]
Type=simple
User=nobody
ExecStart=/usr/local/bin/node_exporter
Restart=on-failure

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable node_exporter
    systemctl start node_exporter
    
    echo -e "${GREEN}? Node Exporter installed${NC}"
fi

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Deployment Complete!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Copy your application files to:"
echo "   - API: $INSTALL_DIR/api"
echo "   - Worker: $INSTALL_DIR/worker"
echo "   - Web: $INSTALL_DIR/web"
echo ""
echo "2. Configure application settings:"
echo "   - Edit appsettings.Production.json in each directory"
echo "   - Set connection strings, Key Vault URLs, etc."
echo ""
echo "3. Obtain SSL certificates:"
echo "   sudo certbot --nginx -d api.hartonomous.com -d app.hartonomous.com"
echo ""
echo "4. Start services:"
echo "   sudo systemctl start hartonomous-api"
echo "   sudo systemctl start hartonomous-worker"
echo "   sudo systemctl start nginx"
echo ""
echo "5. Enable services to start on boot:"
echo "   sudo systemctl enable hartonomous-api"
echo "   sudo systemctl enable hartonomous-worker"
echo "   sudo systemctl enable nginx"
echo ""
echo "6. Check service status:"
echo "   sudo systemctl status hartonomous-api"
echo "   sudo systemctl status hartonomous-worker"
echo ""
echo "7. View logs:"
echo "   sudo journalctl -u hartonomous-api -f"
echo "   sudo journalctl -u hartonomous-worker -f"
echo ""
echo -e "${GREEN}Server is ready for deployment!${NC}"
