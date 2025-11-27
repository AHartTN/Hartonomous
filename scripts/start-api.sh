#!/bin/bash
# ============================================================================
# Start FastAPI Development Server
# ============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

cd "$PROJECT_ROOT"

# Activate venv if exists
if [ -f "venv/bin/activate" ]; then
    source venv/bin/activate
fi

# Set environment variables for local dev
export PGHOST=localhost
export PGPORT=5432
export PGUSER=hartonomous
export PGDATABASE=hartonomous
export PGPASSWORD=${PGPASSWORD:-"Revolutionary-AI-2025!Geometry"}

export NEO4J_ENABLED=true
export NEO4J_URI=bolt://localhost:7687
export NEO4J_USER=neo4j
export NEO4J_PASSWORD=${NEO4J_PASSWORD:-neo4jneo4j}

export LOG_LEVEL=DEBUG
export AUTH_ENABLED=false
export USE_AZURE_CONFIG=false

export CODE_ATOMIZER_URL=http://localhost:8080

echo "🚀 Starting FastAPI development server..."
echo "   API: http://localhost:8000"
echo "   Docs: http://localhost:8000/docs"
echo ""

uvicorn api.main:app \
    --host 0.0.0.0 \
    --port 8000 \
    --reload \
    --log-level debug
