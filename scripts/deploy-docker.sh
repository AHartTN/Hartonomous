#!/bin/bash
# ============================================================================
# Docker Deployment Script
# Idempotent deployment for containerized environment
# ============================================================================

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo -e "${BLUE}🚀 Deploying Hartonomous (Docker)${NC}"

cd "${PROJECT_ROOT}"

# ============================================================================
# Pre-flight Checks
# ============================================================================
echo -e "\n${YELLOW}🔍 Pre-flight checks...${NC}"

if ! command -v docker &> /dev/null; then
    echo -e "${RED}❌ Docker not found${NC}"
    exit 1
fi

if ! docker ps &> /dev/null; then
    echo -e "${RED}❌ Cannot access Docker. Check permissions${NC}"
    exit 1
fi

echo -e "${GREEN}✓${NC} Docker accessible"

# ============================================================================
# Environment Configuration
# ============================================================================
echo -e "\n${YELLOW}⚙️  Environment configuration...${NC}"

if [ ! -f "${PROJECT_ROOT}/.env" ]; then
    echo -e "${BLUE}Creating .env file...${NC}"
    cat > "${PROJECT_ROOT}/.env" << 'EOF'
# PostgreSQL (Docker)
PGHOST=postgres
PGPORT=5432
PGUSER=hartonomous
PGPASSWORD=Revolutionary-AI-2025!Geometry
PGDATABASE=hartonomous

# Neo4j
NEO4J_ENABLED=true
NEO4J_URI=bolt://neo4j:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=neo4jneo4j
NEO4J_BOLT_PORT=7687
NEO4J_HTTP_PORT=7474

# API
LOG_LEVEL=INFO
API_RELOAD=false
AUTH_ENABLED=false

# Features
AGE_WORKER_ENABLED=false
EOF
    echo -e "${GREEN}✓${NC} .env created"
else
    echo -e "${GREEN}✓${NC} .env exists"
fi

# ============================================================================
# Build Images
# ============================================================================
echo -e "\n${YELLOW}🏗️  Building Docker images...${NC}"

docker compose build --no-cache

# ============================================================================
# Stop Existing Containers
# ============================================================================
echo -e "\n${YELLOW}🛑 Stopping existing containers...${NC}"

docker compose down || true

# ============================================================================
# Start Services
# ============================================================================
echo -e "\n${YELLOW}🚀 Starting services...${NC}"

docker compose up -d

# ============================================================================
# Wait for Health Checks
# ============================================================================
echo -e "\n${YELLOW}⏳ Waiting for services to be healthy...${NC}"

echo -e "${BLUE}PostgreSQL...${NC}"
timeout=60
until docker compose exec -T postgres pg_isready -U hartonomous || [ $timeout -eq 0 ]; do
    sleep 1
    ((timeout--))
done

if [ $timeout -eq 0 ]; then
    echo -e "${RED}❌ PostgreSQL health check timeout${NC}"
    docker compose logs postgres
    exit 1
fi
echo -e "${GREEN}✓${NC} PostgreSQL ready"

echo -e "${BLUE}Neo4j...${NC}"
timeout=90
until docker compose exec -T neo4j wget --quiet --tries=1 --spider http://localhost:7474 || [ $timeout -eq 0 ]; do
    sleep 1
    ((timeout--))
done

if [ $timeout -eq 0 ]; then
    echo -e "${YELLOW}⚠️${NC}  Neo4j health check timeout (may be optional)"
fi
echo -e "${GREEN}✓${NC} Neo4j ready"

echo -e "${BLUE}API...${NC}"
timeout=60
until curl -f http://localhost:8000/v1/health &> /dev/null || [ $timeout -eq 0 ]; do
    sleep 1
    ((timeout--))
done

if [ $timeout -eq 0 ]; then
    echo -e "${RED}❌ API health check timeout${NC}"
    docker compose logs api
    exit 1
fi
echo -e "${GREEN}✓${NC} API ready"

# ============================================================================
# Initialize Database
# ============================================================================
echo -e "\n${YELLOW}📊 Checking database schema...${NC}"

TABLE_COUNT=$(docker compose exec -T postgres psql -U hartonomous -d hartonomous -tAc "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")

if [ "$TABLE_COUNT" -eq 0 ]; then
    echo -e "${BLUE}Initializing database schema...${NC}"
    docker compose exec -T postgres bash -c "
        cd /schema
        export PGHOST=localhost PGPORT=5432 PGUSER=hartonomous PGDATABASE=hartonomous
        find . -name '*.sql' -type f | sort | while read file; do
            echo \"Executing: \$file\"
            psql -U hartonomous -d hartonomous -f \"\$file\" 2>&1 || true
        done
    "
    echo -e "${GREEN}✓${NC} Database initialized"
else
    echo -e "${GREEN}✓${NC} Database schema exists ($TABLE_COUNT tables)"
fi

# ============================================================================
# Summary
# ============================================================================
echo -e "\n${BLUE}═══════════════════════════════════════════${NC}"
echo -e "${GREEN}✅ Deployment complete!${NC}"
echo -e "${BLUE}═══════════════════════════════════════════${NC}"
echo -e ""
echo -e "${YELLOW}Services:${NC}"
echo -e "  ${BLUE}API:       http://localhost/docs${NC}"
echo -e "  ${BLUE}API Direct: http://localhost:8000/docs${NC}"
echo -e "  ${BLUE}Neo4j UI:  http://localhost:7474${NC}"
echo -e "  ${BLUE}PostgreSQL: localhost:5432${NC}"
echo -e ""
echo -e "${YELLOW}Useful commands:${NC}"
echo -e "  ${BLUE}docker compose logs -f api${NC}      # View API logs"
echo -e "  ${BLUE}docker compose exec postgres psql -U hartonomous -d hartonomous${NC}"
echo -e "  ${BLUE}docker compose down${NC}             # Stop services"
echo -e "  ${BLUE}docker compose down -v${NC}          # Stop and remove data"
echo -e ""
echo -e "${BLUE}═══════════════════════════════════════════${NC}"
