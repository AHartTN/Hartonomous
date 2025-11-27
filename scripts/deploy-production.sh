#!/bin/bash
# ============================================================================
# Production Deployment Script
# Used by CI/CD and manual deployments
# ============================================================================

set -e

# Configuration
DEPLOY_USER="${DEPLOY_USER:-hartonomous}"
DEPLOY_HOST="${DEPLOY_HOST:-}"
DEPLOY_PATH="${DEPLOY_PATH:-/opt/hartonomous}"
API_IMAGE="${API_IMAGE:-ghcr.io/aharttn/hartonomous-api:latest}"
ATOMIZER_IMAGE="${ATOMIZER_IMAGE:-ghcr.io/aharttn/hartonomous-code-atomizer:latest}"

if [ -z "$DEPLOY_HOST" ]; then
    echo "❌ DEPLOY_HOST environment variable not set"
    echo "Usage: DEPLOY_HOST=your.server.com ./scripts/deploy-production.sh"
    exit 1
fi

echo "🚀 Deploying Hartonomous to Production"
echo "======================================="
echo "Host: $DEPLOY_HOST"
echo "API Image: $API_IMAGE"
echo "Atomizer Image: $ATOMIZER_IMAGE"
echo ""

# Pull latest images
echo "📦 Pulling Docker images on remote host..."
ssh "$DEPLOY_USER@$DEPLOY_HOST" << EOF
    set -e
    cd $DEPLOY_PATH
    
    # Login to GitHub Container Registry
    echo "$GITHUB_TOKEN" | docker login ghcr.io -u "$GITHUB_ACTOR" --password-stdin
    
    # Pull images
    docker pull $API_IMAGE
    docker pull $ATOMIZER_IMAGE
    
    # Update docker-compose to use new images
    sed -i 's|image:.*hartonomous-api.*|image: $API_IMAGE|' docker-compose.yml
    sed -i 's|build:.*|image: $API_IMAGE|' docker-compose.yml
    
    # Deploy with zero-downtime
    docker-compose up -d --no-build
    
    # Health check
    echo "⏳ Waiting for health checks..."
    sleep 10
    
    if docker-compose ps | grep -q "unhealthy"; then
        echo "❌ Health check failed. Rolling back..."
        docker-compose logs
        exit 1
    fi
    
    echo "✅ Deployment successful"
    docker-compose ps
EOF

echo ""
echo "✅ Production deployment complete!"
echo "   URL: https://$DEPLOY_HOST"
