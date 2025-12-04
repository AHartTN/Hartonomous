#!/bin/bash
#############################################################################
# Hartonomous Application Deployment Script
# Deploys built application to HART-SERVER
# Usage: ./deploy-app.sh [production|staging|development] [api|worker|web|all]
#############################################################################

set -e

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Configuration
REMOTE_HOST="${REMOTE_HOST:-hart-server}"
REMOTE_USER="${REMOTE_USER:-www-data}"

# Environment (first argument)
ENVIRONMENT="${1:-production}"

# Determine deployment directory based on environment
case $ENVIRONMENT in
    production)
        DEPLOY_DIR="/srv/www/production"
        BUILD_CONFIG="Release"
        ;;
    staging)
        DEPLOY_DIR="/srv/www/staging"
        BUILD_CONFIG="Release"
        ;;
    development|dev)
        DEPLOY_DIR="/srv/www/development"
        BUILD_CONFIG="Debug"
        ;;
    *)
        echo -e "${RED}Invalid environment: $ENVIRONMENT${NC}"
        echo "Usage: $0 [production|staging|development] [api|worker|web|all]"
        exit 1
        ;;
esac

BUILD_DIR="./artifacts/${ENVIRONMENT^}"

# Component to deploy (second argument, default to all)
COMPONENT="${2:-all}"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Deploying Hartonomous to $REMOTE_HOST${NC}"
echo -e "${GREEN}Environment: $ENVIRONMENT${NC}"
echo -e "${GREEN}========================================${NC}"

# Function to build application
build_component() {
    local component=$1
    
    echo -e "${YELLOW}Building $component for $ENVIRONMENT...${NC}"
    
    case $component in
        api)
            dotnet publish Hartonomous.API/Hartonomous.API.csproj \
                -c $BUILD_CONFIG \
                -o "$BUILD_DIR/api" \
                -p:EnvironmentName=$ENVIRONMENT
            ;;
        worker)
            dotnet publish Hartonomous.Worker/Hartonomous.Worker.csproj \
                -c $BUILD_CONFIG \
                -o "$BUILD_DIR/worker" \
                -p:EnvironmentName=$ENVIRONMENT
            ;;
        web)
            dotnet publish Hartonomous.App/Hartonomous.App.Web/Hartonomous.App.Web.csproj \
                -c $BUILD_CONFIG \
                -o "$BUILD_DIR/web" \
                -p:EnvironmentName=$ENVIRONMENT
            ;;
    esac
    
    echo -e "${GREEN}? $component built${NC}"
}

# Function to deploy component
deploy_component() {
    local component=$1
    local service_name="hartonomous-$component-$ENVIRONMENT"
    
    echo -e "${YELLOW}Deploying $component to $ENVIRONMENT...${NC}"
    
    # Stop service
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo systemctl stop $service_name" 2>/dev/null || true
    
    # Backup current version
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo cp -r $DEPLOY_DIR/$component $DEPLOY_DIR/${component}.backup.$(date +%Y%m%d-%H%M%S)" 2>/dev/null || true
    
    # Create temporary directory on remote
    ssh "$REMOTE_USER@$REMOTE_HOST" "mkdir -p /tmp/hartonomous-$component"
    
    # Copy files
    echo -e "${YELLOW}Copying files to $REMOTE_HOST...${NC}"
    scp -r "$BUILD_DIR/$component/"* "$REMOTE_USER@$REMOTE_HOST:/tmp/hartonomous-$component/"
    
    # Move files to deployment directory
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo rm -rf $DEPLOY_DIR/$component/*"
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo mv /tmp/hartonomous-$component/* $DEPLOY_DIR/$component/"
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo rm -rf /tmp/hartonomous-$component"
    
    # Set permissions
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo chown -R www-data:www-data $DEPLOY_DIR/$component"
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo chmod -R 755 $DEPLOY_DIR/$component"
    
    # Start service
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo systemctl start $service_name"
    
    # Check status
    sleep 5
    ssh "$REMOTE_USER@$REMOTE_HOST" "sudo systemctl status $service_name --no-pager" || true
    
    echo -e "${GREEN}? $component deployed to $ENVIRONMENT${NC}"
}

# Build and deploy based on arguments
case $COMPONENT in
    api)
        build_component "api"
        deploy_component "api"
        ;;
    worker)
        build_component "worker"
        deploy_component "worker"
        ;;
    web)
        build_component "web"
        deploy_component "web"
        ;;
    all)
        build_component "api"
        build_component "worker"
        build_component "web"
        
        deploy_component "api"
        deploy_component "worker"
        deploy_component "web"
        ;;
    *)
        echo -e "${RED}Invalid component: $COMPONENT${NC}"
        echo "Usage: $0 [production|staging|development] [api|worker|web|all]"
        exit 1
        ;;
esac

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Deployment Complete!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo "Deployed to: $DEPLOY_DIR"
echo "Services:"
if [ "$COMPONENT" = "all" ] || [ "$COMPONENT" = "api" ]; then
    echo "  - hartonomous-api-$ENVIRONMENT"
fi
if [ "$COMPONENT" = "all" ] || [ "$COMPONENT" = "worker" ]; then
    echo "  - hartonomous-worker-$ENVIRONMENT"
fi
echo ""
echo "View logs:"
if [ "$COMPONENT" = "all" ] || [ "$COMPONENT" = "api" ]; then
    echo "  sudo journalctl -u hartonomous-api-$ENVIRONMENT -f"
fi
if [ "$COMPONENT" = "all" ] || [ "$COMPONENT" = "worker" ]; then
    echo "  sudo journalctl -u hartonomous-worker-$ENVIRONMENT -f"
fi
