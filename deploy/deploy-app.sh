#!/bin/bash
#############################################################################
# Hartonomous Application Deployment Script
# Deploys built application to HART-SERVER
# Usage: ./deploy-app.sh [api|worker|web|all]
#############################################################################

set -e

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Configuration
REMOTE_HOST="${REMOTE_HOST:-hart-server}"
REMOTE_USER="${REMOTE_USER:-hartonomous}"
INSTALL_DIR="/opt/hartonomous"
BUILD_DIR="./artifacts/Production"

# Component to deploy
COMPONENT="${1:-all}"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Deploying Hartonomous to $REMOTE_HOST${NC}"
echo -e "${GREEN}========================================${NC}"

deploy_component() {
    local component=$1
    local service_name="hartonomous-$component"
    
    echo -e "${YELLOW}Deploying $component...${NC}"
    
    # Stop service
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo systemctl stop $service_name" || true
    
    # Backup current version
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo mv $INSTALL_DIR/$component $INSTALL_DIR/${component}.backup.$(date +%Y%m%d-%H%M%S)" || true
    
    # Create directory
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo mkdir -p $INSTALL_DIR/$component"
    
    # Copy files
    if [ "$component" = "api" ]; then
        scp -r "$BUILD_DIR/Hartonomous.API/net10.0/"* "$REMOTE_USER@$REMOTE_HOST:/tmp/hartonomous-$component/"
    elif [ "$component" = "worker" ]; then
        scp -r "$BUILD_DIR/Hartonomous.Worker/net10.0/"* "$REMOTE_USER@$REMOTE_HOST:/tmp/hartonomous-$component/"
    elif [ "$component" = "web" ]; then
        scp -r "$BUILD_DIR/Hartonomous.App.Web/net10.0/"* "$REMOTE_USER@$REMOTE_HOST:/tmp/hartonomous-$component/"
    fi
    
    # Move files to install directory
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo mv /tmp/hartonomous-$component/* $INSTALL_DIR/$component/"
    
    # Set permissions
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo chown -R hartonomous:hartonomous $INSTALL_DIR/$component"
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo chmod -R 755 $INSTALL_DIR/$component"
    
    # Start service
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo systemctl start $service_name"
    
    # Check status
    sleep 5
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo systemctl status $service_name --no-pager" || true
    
    echo -e "${GREEN}? $component deployed${NC}"
}

# Deploy based on argument
case $COMPONENT in
    api)
        deploy_component "api"
        ;;
    worker)
        deploy_component "worker"
        ;;
    web)
        deploy_component "web"
        ;;
    all)
        deploy_component "api"
        deploy_component "worker"
        deploy_component "web"
        ;;
    *)
        echo -e "${RED}Invalid component: $COMPONENT${NC}"
        echo "Usage: $0 [api|worker|web|all]"
        exit 1
        ;;
esac

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Deployment Complete!${NC}"
echo -e "${GREEN}========================================${NC}"
