#!/bin/bash
# API Application Deployment Script (Bash)
# Deploys Hartonomous FastAPI application
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Parse arguments
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"
SKIP_BACKUP=false
SKIP_DEPENDENCIES=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        --skip-backup)
            SKIP_BACKUP=true
            shift
            ;;
        --skip-dependencies)
            SKIP_DEPENDENCIES=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "API Application Deployment"

# Validate environment
if [[ -z "$ENVIRONMENT" ]]; then
    write_failure "DEPLOYMENT_ENVIRONMENT not set. Use -e parameter or set environment variable."
fi

case $ENVIRONMENT in
    development|staging|production)
        ;;
    *)
        write_failure "Invalid environment: $ENVIRONMENT"
        ;;
esac

# Load configuration
load_deployment_config "$ENVIRONMENT"
write_log "Loaded configuration for: $ENVIRONMENT" "INFO"

# Get repository root
REPO_ROOT="$SCRIPT_DIR/../../.."
API_PATH="$REPO_ROOT/api"

if [[ ! -d "$API_PATH" ]]; then
    write_failure "API directory not found: $API_PATH"
fi

write_log "API path: $API_PATH" "INFO"

# Backup existing deployment (unless skipped)
if [[ "$SKIP_BACKUP" != "true" ]]; then
    write_step "Creating Pre-Deployment Backup"
    "$SCRIPT_DIR/backup-application.sh" -e "$ENVIRONMENT" || write_log "Backup failed, but continuing..." "WARNING"
fi

# Install/Update dependencies
if [[ "$SKIP_DEPENDENCIES" != "true" ]]; then
    write_step "Installing Python Dependencies"

    cd "$API_PATH"

    # Check if virtual environment exists
    if [[ ! -d ".venv" ]]; then
        write_log "Creating virtual environment..." "INFO"
        python3 -m venv .venv
    fi

    # Activate virtual environment
    source .venv/bin/activate

    # Install requirements
    write_log "Installing requirements..." "INFO"
    python -m pip install --upgrade pip
    python -m pip install -r requirements.txt

    write_success "Dependencies installed"

    cd - > /dev/null
fi

# Create .env file for environment
write_step "Configuring Environment Variables"

ENV_FILE="$API_PATH/.env"
DB_HOST=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.host')
DB_PORT=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.port')
DB_NAME=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.name')
DB_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.database.user')
API_HOST=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.api.host')
API_PORT=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.api.port')
API_WORKERS=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.api.workers')
API_RELOAD=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.api.reload')
LOG_LEVEL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.logging.level')
NEO4J_ENABLED=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.features.neo4j_enabled')
AGE_WORKER_ENABLED=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.features.age_worker_enabled')
AUTH_ENABLED=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.features.auth_enabled')

cat > "$ENV_FILE" <<EOF
# Auto-generated .env file for $ENVIRONMENT
# Generated: $(date '+%Y-%m-%d %H:%M:%S')

DEPLOYMENT_ENVIRONMENT=$ENVIRONMENT
LOG_LEVEL=$LOG_LEVEL

# Database Configuration
PGHOST=$DB_HOST
PGPORT=$DB_PORT
PGDATABASE=$DB_NAME
PGUSER=$DB_USER

# API Configuration
API_HOST=$API_HOST
API_PORT=$API_PORT
API_WORKERS=$API_WORKERS
API_RELOAD=$API_RELOAD

# Feature Flags
NEO4J_ENABLED=$NEO4J_ENABLED
AGE_WORKER_ENABLED=$AGE_WORKER_ENABLED
AUTH_ENABLED=$AUTH_ENABLED
EOF

# Add Neo4j config if enabled
if [[ "$NEO4J_ENABLED" == "true" ]]; then
    NEO4J_URI=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.uri')
    NEO4J_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.user')
    NEO4J_DATABASE=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.database')

    cat >> "$ENV_FILE" <<EOF

# Neo4j Configuration
NEO4J_URI=$NEO4J_URI
NEO4J_USER=$NEO4J_USER
NEO4J_DATABASE=$NEO4J_DATABASE
EOF
fi

# Add Azure AD config if auth enabled
if [[ "$AUTH_ENABLED" == "true" ]]; then
    TENANT_ID=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.tenant_id')
    CLIENT_ID=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.client_id')

    cat >> "$ENV_FILE" <<EOF

# Azure AD Configuration
AZURE_AD_TENANT_ID=$TENANT_ID
AZURE_AD_CLIENT_ID=$CLIENT_ID
EOF
fi

write_success "Environment file created: $ENV_FILE"

# Get secrets from Azure Key Vault for non-development environments
if [[ "$ENVIRONMENT" != "development" ]]; then
    KV_URL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.key_vault_url')

    if [[ "$KV_URL" != "null" ]]; then
        write_step "Retrieving Secrets from Azure Key Vault"

        # Authenticate to Azure
        azure_login

        KV_NAME=$(echo "$KV_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')

        # Get database password
        DB_PASSWORD=$(get_keyvault_secret "$KV_NAME" "PostgreSQL-$DB_NAME-Password")
        echo "PGPASSWORD=$DB_PASSWORD" >> "$ENV_FILE"

        # Get Neo4j password if enabled
        if [[ "$NEO4J_ENABLED" == "true" ]]; then
            MACHINE=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.target.machine')
            NEO4J_PASSWORD=$(get_keyvault_secret "$KV_NAME" "Neo4j-$MACHINE-Password")
            echo "NEO4J_PASSWORD=$NEO4J_PASSWORD" >> "$ENV_FILE"
        fi

        write_success "Secrets retrieved from Key Vault"
    fi
else
    write_log "Using local environment variables for secrets (development)" "INFO"
fi

# Run database migrations (schema deployment)
write_step "Running Database Migrations"
"$SCRIPT_DIR/../database/deploy-schema.sh" -e "$ENVIRONMENT" --skip-backup

# Stop existing service (if running)
write_step "Stopping Existing Service"

SERVICE_NAME="hartonomous-api-$ENVIRONMENT"

if systemctl is-active --quiet "$SERVICE_NAME"; then
    write_log "Stopping service: $SERVICE_NAME" "INFO"
    sudo systemctl stop "$SERVICE_NAME"
    sleep 2
    write_success "Service stopped"
else
    write_log "Service not running or not installed: $SERVICE_NAME" "INFO"
fi

# Start API (development: foreground, staging/prod: systemd service)
write_step "Starting API Application"

if [[ "$ENVIRONMENT" == "development" ]]; then
    write_success "Development environment - API ready to start manually"
    write_log "To start API, run:" "INFO"
    echo -e "  cd api"
    echo -e "  source .venv/bin/activate"
    echo -e "  python -m uvicorn main:app --reload"
else
    # Production/Staging: Start as systemd service
    write_log "Starting API as systemd service..." "INFO"

    # Create systemd service file
    SERVICE_FILE="/etc/systemd/system/$SERVICE_NAME.service"

    sudo tee "$SERVICE_FILE" > /dev/null <<EOF
[Unit]
Description=Hartonomous API - $ENVIRONMENT
After=network.target postgresql.service

[Service]
Type=exec
User=$(whoami)
WorkingDirectory=$API_PATH
Environment="PATH=$API_PATH/.venv/bin:/usr/local/bin:/usr/bin:/bin"
EnvironmentFile=$ENV_FILE
ExecStart=$API_PATH/.venv/bin/uvicorn main:app --host $API_HOST --port $API_PORT --workers $API_WORKERS
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

    # Reload systemd and start service
    sudo systemctl daemon-reload
    sudo systemctl enable "$SERVICE_NAME"
    sudo systemctl start "$SERVICE_NAME"

    sleep 2

    if systemctl is-active --quiet "$SERVICE_NAME"; then
        write_success "API service started: $SERVICE_NAME"
    else
        write_failure "Failed to start API service"
    fi
fi

# Summary
write_step "Deployment Summary"
write_success "API deployment completed for: $ENVIRONMENT"
write_log "API path: $API_PATH" "INFO"
write_log "Configuration: $ENV_FILE" "INFO"

if [[ "$ENVIRONMENT" == "development" ]]; then
    echo ""
    echo "Next steps:"
    echo "1. cd api"
    echo "2. source .venv/bin/activate"
    echo "3. python -m uvicorn main:app --reload"
    echo "4. Test: http://localhost:$API_PORT/health"
fi

write_log "API application deployment completed" "INFO"
