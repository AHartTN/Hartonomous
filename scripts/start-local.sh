#!/bin/bash
set -e

echo "🚀 Starting Hartonomous locally..."

# Load environment
if [ -f .env ]; then
    export $(cat .env | grep -v '^#' | xargs)
fi

API_PORT=${API_PORT:-8000}
API_HOST=${API_HOST:-0.0.0.0}

# Check services
echo "🔍 Checking services..."
services_ok=true

if ! systemctl is-active --quiet postgresql; then
    echo "❌ PostgreSQL is not running"
    services_ok=false
fi

if ! systemctl is-active --quiet neo4j; then
    echo "⚠️  Neo4j is not running"
fi

if ! docker ps &> /dev/null; then
    echo "⚠️  Docker not accessible (may need 'newgrp docker' or logout/login)"
fi

if [ "$services_ok" = false ]; then
    echo "❌ Required services not running. Run: sudo ./scripts/setup-local-dev.sh"
    exit 1
fi

# Activate virtual environment or use system python3.13
if [ -d "venv" ]; then
    source venv/bin/activate
    echo "✅ Using virtual environment"
elif command -v python3.13 &> /dev/null; then
    echo "✅ Using system Python 3.13"
else
    echo "❌ Python 3.13 not found"
    exit 1
fi

# Start API
echo "🌐 Starting API on $API_HOST:$API_PORT..."
echo ""
echo "API will be available at: http://localhost:$API_PORT"
echo "Docs available at: http://localhost:$API_PORT/docs"
echo ""

uvicorn api.main:app --host $API_HOST --port $API_PORT --reload
