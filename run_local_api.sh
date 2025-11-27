#!/bin/bash
# Local Development API Server
# Uses system PostgreSQL via Unix socket (no password needed)

cd "$(dirname "$0")"

# Load local env
export $(cat .env.local | grep -v '^#' | xargs)

echo "🚀 Starting Hartonomous API (Local Development Mode)"
echo "   Database: PostgreSQL 16 (Unix socket)"
echo "   Port: $API_PORT"
echo "   Docs: http://localhost:$API_PORT/docs"
echo ""

uvicorn api.main:app \
  --host $API_HOST \
  --port $API_PORT \
  --reload \
  --log-level info
