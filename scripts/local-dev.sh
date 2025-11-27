#!/bin/bash
# ============================================================================
# Local Development Environment Setup & Start
# Idempotent script for local development
# ============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

cd "$PROJECT_ROOT"

echo "🚀 Hartonomous Local Development Environment"
echo "=============================================="
echo ""

# Ensure docker permissions
if ! docker ps > /dev/null 2>&1; then
    echo "⚠️  Docker access denied. Setting up permissions..."
    "$SCRIPT_DIR/setup-docker-permissions.sh"
    echo "⚠️  Please run: newgrp docker"
    echo "   Then re-run this script"
    exit 1
fi

# Check if Python 3.13 is available
if ! command -v python3.13 > /dev/null 2>&1; then
    echo "⚠️  Python 3.13 not found. Installing..."
    sudo "$SCRIPT_DIR/../tmp/setup-python313-fixed.sh" || {
        echo "❌ Python 3.13 installation failed"
        exit 1
    }
fi

echo "✅ Python 3.13 available"
python3.13 --version

# Install Python dependencies if needed
if [ ! -d "venv" ] || [ ! -f "venv/bin/activate" ]; then
    echo "📦 Creating Python virtual environment..."
    python3.13 -m venv venv
fi

echo "📦 Installing Python dependencies..."
source venv/bin/activate
pip install --upgrade pip > /dev/null
pip install -r api/requirements.txt > /dev/null

# Check PostgreSQL connection
echo "🔍 Checking PostgreSQL..."
if psql -U hartonomous -d hartonomous -c "SELECT 1" > /dev/null 2>&1; then
    echo "✅ PostgreSQL connection OK"
else
    echo "⚠️  PostgreSQL connection issue. Fixing authentication..."
    "$SCRIPT_DIR/fix-pg-auth.sh"
fi

# Initialize database if needed
echo "🔍 Checking database schema..."
if ! psql -U hartonomous -d hartonomous -c "SELECT 1 FROM atoms LIMIT 1" > /dev/null 2>&1; then
    echo "📊 Initializing database schema..."
    "$SCRIPT_DIR/init-database.sh"
else
    echo "✅ Database schema exists"
fi

# Start Docker services
echo "🐳 Starting Docker services..."
docker-compose up -d postgres neo4j

# Wait for services
echo "⏳ Waiting for services to be healthy..."
timeout=60
elapsed=0
while [ $elapsed -lt $timeout ]; do
    if docker-compose ps | grep -q "healthy"; then
        break
    fi
    sleep 2
    elapsed=$((elapsed + 2))
done

if [ $elapsed -ge $timeout ]; then
    echo "⚠️  Services took too long to start. Check logs:"
    echo "   docker-compose logs"
    exit 1
fi

echo "✅ Services ready"
echo ""
echo "🎉 Local development environment ready!"
echo ""
echo "Available commands:"
echo "  - Start API:       ./scripts/start-api.sh"
echo "  - Run tests:       pytest api/tests/"
echo "  - View logs:       docker-compose logs -f"
echo "  - Stop services:   docker-compose down"
echo "  - PostgreSQL CLI:  psql -U hartonomous -d hartonomous"
echo "  - Neo4j Browser:   http://localhost:7474"
echo ""
