#!/bin/bash
# Platform-agnostic deployment script
# Uses Azure App Config + Key Vault for all configuration
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -e

ENV=$1
ARTIFACT=$2

if [ -z "$ENV" ] || [ -z "$ARTIFACT" ]; then
    echo "Usage: deploy.sh <environment> <artifact_path>"
    exit 1
fi

echo "?? Deploying to $ENV environment..."

# Get App Config endpoint
APP_CONFIG_ENDPOINT=$(az appconfig show \
    --name "$APP_CONFIG_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query endpoint -o tsv)

echo "?? Loading configuration from Azure App Configuration..."

# Get configuration (Key Vault references are auto-resolved by Azure CLI)
INSTALL_PATH=$(az appconfig kv show \
    --endpoint "$APP_CONFIG_ENDPOINT" \
    --key "deployment:$ENV:install_path" \
    --query value -o tsv)

API_HOST=$(az appconfig kv show \
    --endpoint "$APP_CONFIG_ENDPOINT" \
    --key "api:$ENV:host" \
    --query value -o tsv)

API_PORT=$(az appconfig kv show \
    --endpoint "$APP_CONFIG_ENDPOINT" \
    --key "api:$ENV:port" \
    --query value -o tsv)

# Database connection string (stored as Key Vault reference in App Config)
DB_URL=$(az appconfig kv show \
    --endpoint "$APP_CONFIG_ENDPOINT" \
    --key "database:$ENV:connection_string" \
    --query value -o tsv)

echo "?? Installing to: $INSTALL_PATH"
echo "?? API: $API_HOST:$API_PORT"

# Create installation directory
mkdir -p "$INSTALL_PATH"

# Extract artifact
echo "?? Extracting application..."
unzip -q "$ARTIFACT" -d "$INSTALL_PATH"

# Setup Python environment
cd "$INSTALL_PATH"

# Detect Python command (cross-platform)
if command -v python3 &> /dev/null; then
    PYTHON_CMD="python3"
elif command -v python &> /dev/null; then
    PYTHON_CMD="python"
else
    echo "? Python not found"
    exit 1
fi

echo "?? Setting up Python environment..."
$PYTHON_CMD -m venv .venv

# Activate virtual environment (cross-platform)
if [ -f ".venv/bin/activate" ]; then
    source .venv/bin/activate
elif [ -f ".venv/Scripts/activate" ]; then
    source .venv/Scripts/activate
else
    echo "? Cannot activate virtual environment"
    exit 1
fi

# Install dependencies
echo "?? Installing dependencies..."
pip install -q --upgrade pip
pip install -q -r requirements.txt

# Create .env file
echo "?? Configuring environment..."
cat > .env << EOF
DATABASE_URL=$DB_URL
API_HOST=$API_HOST
API_PORT=$API_PORT
ENVIRONMENT=$ENV
EOF

# Run database migrations
echo "??? Running database migrations..."
if [ -f "alembic.ini" ]; then
    alembic upgrade head
fi

# Restart service
echo "?? Restarting service..."

# Kill existing process
pkill -f "uvicorn main:app" || true
sleep 2

# Start new process
nohup $PYTHON_CMD -m uvicorn main:app --host "$API_HOST" --port "$API_PORT" > /dev/null 2>&1 &
NEW_PID=$!

echo "? Service started (PID: $NEW_PID)"

# Verify deployment
echo "?? Verifying deployment..."
sleep 3

HEALTH_URL="http://$API_HOST:$API_PORT/health"
if curl -f -s "$HEALTH_URL" > /dev/null 2>&1; then
    echo "? Health check passed"
    echo "?? Deployment to $ENV complete!"
    exit 0
else
    echo "?? Health check failed - service may still be starting"
    exit 1
fi
