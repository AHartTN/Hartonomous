#!/bin/bash
#
# Idempotent systemd services setup for Hartonomous
# Creates service files, directories, and permissions
# Safe to run multiple times
#

set -e

ENVIRONMENT="${1:-production}"

echo "========================================"
echo "Hartonomous systemd Services Setup"
echo "Environment: $ENVIRONMENT"
echo "========================================"

# Configuration based on environment
case "$ENVIRONMENT" in
    production)
        DEPLOY_DIR="/srv/www/production"
        API_PORT=5000
        WEB_PORT=5001
        ;;
    staging)
        DEPLOY_DIR="/srv/www/staging"
        API_PORT=5010
        WEB_PORT=5011
        ;;
    development)
        DEPLOY_DIR="/srv/www/development"
        API_PORT=5020
        WEB_PORT=5021
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

# Function to create directory if it doesn't exist
ensure_directory() {
    local dir="$1"
    local owner="$2"
    local perms="$3"
    
    if [ ! -d "$dir" ]; then
        echo "Creating directory: $dir"
        mkdir -p "$dir"
        chown "$owner:$owner" "$dir"
        chmod "$perms" "$dir"
        echo "✓ Created directory"
    else
        echo "✓ Directory exists: $dir"
        # Ensure correct ownership and permissions (idempotent)
        chown "$owner:$owner" "$dir"
        chmod "$perms" "$dir"
    fi
}

# Function to create or update systemd service file
ensure_service() {
    local service_name="$1"
    local description="$2"
    local working_dir="$3"
    local exec_start="$4"
    local environment_vars="$5"
    
    local service_file="/etc/systemd/system/${service_name}.service"
    
    echo "Configuring service: $service_name"
    
    # Create service file (overwrite if exists for idempotency)
    cat > "$service_file" <<EOF
[Unit]
Description=$description
After=network.target

[Service]
Type=notify
WorkingDirectory=$working_dir
ExecStart=$exec_start
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=$service_name
User=www-data
Group=www-data
$environment_vars

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$working_dir

# Resource limits
LimitNOFILE=65536
LimitNPROC=4096

[Install]
WantedBy=multi-user.target
EOF

    echo "✓ Service file created/updated: $service_file"
    
    # Reload systemd daemon to pick up changes
    systemctl daemon-reload
    
    # Enable service (idempotent)
    systemctl enable "$service_name" 2>/dev/null || true
    echo "✓ Service enabled: $service_name"
}

# Create base directories
echo ""
echo "Creating base directories..."
ensure_directory "/srv/www" "www-data" "755"
ensure_directory "$DEPLOY_DIR" "www-data" "755"
ensure_directory "$DEPLOY_DIR/api" "www-data" "755"
ensure_directory "$DEPLOY_DIR/worker" "www-data" "755"
ensure_directory "$DEPLOY_DIR/web" "www-data" "755"

# Create logs directory
ensure_directory "/var/log/hartonomous" "www-data" "755"
ensure_directory "/var/log/hartonomous/$ENVIRONMENT" "www-data" "755"

# Setup API Service
echo ""
echo "Setting up API service..."
API_SERVICE="hartonomous-api-${ENVIRONMENT}"
API_ENV="Environment=ASPNETCORE_ENVIRONMENT=${ENVIRONMENT}
Environment=ASPNETCORE_URLS=http://localhost:${API_PORT}
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false"

ensure_service "$API_SERVICE" \
    "Hartonomous API ($ENVIRONMENT)" \
    "$DEPLOY_DIR/api" \
    "/usr/bin/dotnet $DEPLOY_DIR/api/Hartonomous.API.dll" \
    "$API_ENV"

# Setup Worker Service
echo ""
echo "Setting up Worker service..."
WORKER_SERVICE="hartonomous-worker-${ENVIRONMENT}"
WORKER_ENV="Environment=DOTNET_ENVIRONMENT=${ENVIRONMENT}
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false"

ensure_service "$WORKER_SERVICE" \
    "Hartonomous Worker ($ENVIRONMENT)" \
    "$DEPLOY_DIR/worker" \
    "/usr/bin/dotnet $DEPLOY_DIR/worker/Hartonomous.Worker.dll" \
    "$WORKER_ENV"

# Setup Web Service
echo ""
echo "Setting up Web service..."
WEB_SERVICE="hartonomous-web-${ENVIRONMENT}"
WEB_ENV="Environment=ASPNETCORE_ENVIRONMENT=${ENVIRONMENT}
Environment=ASPNETCORE_URLS=http://localhost:${WEB_PORT}
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false"

ensure_service "$WEB_SERVICE" \
    "Hartonomous Web ($ENVIRONMENT)" \
    "$DEPLOY_DIR/web" \
    "/usr/bin/dotnet $DEPLOY_DIR/web/Hartonomous.App.Web.dll" \
    "$WEB_ENV"

# Summary
echo ""
echo "========================================"
echo "systemd Services Setup Complete!"
echo "========================================"
echo ""
echo "Services created/updated:"
echo "  - $API_SERVICE (Port: $API_PORT)"
echo "  - $WORKER_SERVICE"
echo "  - $WEB_SERVICE (Port: $WEB_PORT)"
echo ""
echo "Service status:"
systemctl is-enabled "$API_SERVICE" && echo "  ✓ $API_SERVICE enabled" || echo "  ✗ $API_SERVICE disabled"
systemctl is-enabled "$WORKER_SERVICE" && echo "  ✓ $WORKER_SERVICE enabled" || echo "  ✗ $WORKER_SERVICE disabled"
systemctl is-enabled "$WEB_SERVICE" && echo "  ✓ $WEB_SERVICE enabled" || echo "  ✗ $WEB_SERVICE disabled"
echo ""
echo "To start services:"
echo "  sudo systemctl start $API_SERVICE"
echo "  sudo systemctl start $WORKER_SERVICE"
echo "  sudo systemctl start $WEB_SERVICE"
echo ""
echo "To check status:"
echo "  sudo systemctl status $API_SERVICE"
echo ""
echo "✓ Ready for nginx configuration and deployment"
