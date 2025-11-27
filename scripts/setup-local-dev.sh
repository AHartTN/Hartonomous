#!/bin/bash
# ============================================================================
# Local Development Environment Setup
# 
# Idempotent setup for local development with system PostgreSQL
# ============================================================================

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo -e "${BLUE}🚀 Setting up local development environment${NC}"

# Check if running as root
if [ "$EUID" -eq 0 ]; then 
    echo -e "${RED}❌ Do not run as root/sudo${NC}"
    exit 1
fi

# ============================================================================
# Step 1: System Dependencies
# ============================================================================
echo -e "\n${YELLOW}📦 Checking system dependencies...${NC}"

check_command() {
    if command -v "$1" &> /dev/null; then
        echo -e "${GREEN}✓${NC} $1 found"
        return 0
    else
        echo -e "${RED}✗${NC} $1 not found"
        return 1
    fi
}

MISSING_DEPS=0

check_command "python3.13" || MISSING_DEPS=1
check_command "psql" || MISSING_DEPS=1
check_command "docker" || MISSING_DEPS=1
check_command "git" || MISSING_DEPS=1

if [ $MISSING_DEPS -eq 1 ]; then
    echo -e "${YELLOW}⚠️  Missing dependencies. Install with:${NC}"
    echo -e "   ${BLUE}sudo ${SCRIPT_DIR}/install-system-deps.sh${NC}"
    exit 1
fi

# ============================================================================
# Step 2: Docker Permissions
# ============================================================================
echo -e "\n${YELLOW}🐳 Checking Docker permissions...${NC}"

if groups | grep -q docker; then
    echo -e "${GREEN}✓${NC} User in docker group"
else
    echo -e "${YELLOW}⚠️${NC}  Adding user to docker group"
    echo -e "   ${BLUE}Run: sudo usermod -aG docker $USER${NC}"
    echo -e "   ${BLUE}Then: newgrp docker${NC}"
    echo -e "   Or logout and login again"
    exit 1
fi

# Test docker access
if docker ps &> /dev/null; then
    echo -e "${GREEN}✓${NC} Docker access verified"
else
    echo -e "${YELLOW}⚠️${NC}  Cannot access Docker. Try:"
    echo -e "   ${BLUE}newgrp docker${NC}"
    exit 1
fi

# ============================================================================
# Step 3: Python Virtual Environment
# ============================================================================
echo -e "\n${YELLOW}🐍 Setting up Python environment...${NC}"

if [ ! -d "${PROJECT_ROOT}/venv" ]; then
    echo -e "${BLUE}Creating virtual environment...${NC}"
    python3.13 -m venv "${PROJECT_ROOT}/venv"
fi

source "${PROJECT_ROOT}/venv/bin/activate"

echo -e "${BLUE}Upgrading pip...${NC}"
pip install --upgrade pip setuptools wheel -q

echo -e "${BLUE}Installing dependencies...${NC}"
if [ -f "${PROJECT_ROOT}/requirements.txt" ]; then
    pip install -r "${PROJECT_ROOT}/requirements.txt" -q
fi

if [ -f "${PROJECT_ROOT}/requirements-dev.txt" ]; then
    pip install -r "${PROJECT_ROOT}/requirements-dev.txt" -q
fi

echo -e "${GREEN}✓${NC} Python environment ready"

# ============================================================================
# Step 4: Environment Configuration
# ============================================================================
echo -e "\n${YELLOW}⚙️  Configuring environment...${NC}"

if [ ! -f "${PROJECT_ROOT}/.env" ]; then
    echo -e "${BLUE}Creating .env file...${NC}"
    cat > "${PROJECT_ROOT}/.env" << 'EOF'
# PostgreSQL (Local System)
PGHOST=localhost
PGPORT=5432
PGUSER=hartonomous
PGPASSWORD=hartonomous
PGDATABASE=hartonomous

# Neo4j
NEO4J_ENABLED=false
NEO4J_URI=bolt://localhost:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=neo4jneo4j

# API
LOG_LEVEL=DEBUG
API_RELOAD=true
AUTH_ENABLED=false

# Features
AGE_WORKER_ENABLED=false
EOF
    echo -e "${GREEN}✓${NC} .env created"
else
    echo -e "${GREEN}✓${NC} .env exists"
fi

# ============================================================================
# Step 5: PostgreSQL Setup
# ============================================================================
echo -e "\n${YELLOW}🗄️  Checking PostgreSQL...${NC}"

# Check if PostgreSQL is running
if ! sudo systemctl is-active --quiet postgresql; then
    echo -e "${BLUE}Starting PostgreSQL...${NC}"
    sudo systemctl start postgresql
fi

# Check if user exists
if sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='hartonomous'" | grep -q 1; then
    echo -e "${GREEN}✓${NC} Database user exists"
else
    echo -e "${BLUE}Creating database user...${NC}"
    sudo -u postgres createuser -s hartonomous || true
fi

# Check if database exists
if sudo -u postgres psql -lqt | cut -d \| -f 1 | grep -qw hartonomous; then
    echo -e "${GREEN}✓${NC} Database exists"
else
    echo -e "${BLUE}Creating database...${NC}"
    sudo -u postgres createdb -O hartonomous hartonomous
fi

# Ensure trust authentication for local dev
if ! sudo grep -q "^local.*hartonomous.*trust" /etc/postgresql/*/main/pg_hba.conf; then
    echo -e "${BLUE}Configuring PostgreSQL authentication...${NC}"
    "${SCRIPT_DIR}/fix-pg-auth.sh"
fi

# Test connection
if psql -U hartonomous -d hartonomous -c "SELECT 1" &> /dev/null; then
    echo -e "${GREEN}✓${NC} Database connection successful"
else
    echo -e "${RED}❌ Cannot connect to database${NC}"
    exit 1
fi

# ============================================================================
# Step 6: Database Schema
# ============================================================================
echo -e "\n${YELLOW}📊 Checking database schema...${NC}"

TABLE_COUNT=$(psql -U hartonomous -d hartonomous -tAc "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")

if [ "$TABLE_COUNT" -eq 0 ]; then
    echo -e "${BLUE}Initializing database schema...${NC}"
    "${SCRIPT_DIR}/init-database.sh"
else
    echo -e "${GREEN}✓${NC} Database schema exists ($TABLE_COUNT tables)"
fi

# ============================================================================
# Step 7: Helper Scripts
# ============================================================================
echo -e "\n${YELLOW}📝 Creating helper scripts...${NC}"

# Local API runner
cat > "${PROJECT_ROOT}/run_local_api.sh" << 'EOF'
#!/bin/bash
set -e
cd "$(dirname "$0")"
source venv/bin/activate
export $(grep -v '^#' .env | xargs)
uvicorn api.main:app --reload --host 0.0.0.0 --port 8000 --log-level debug
EOF
chmod +x "${PROJECT_ROOT}/run_local_api.sh"

# Database shell
cat > "${PROJECT_ROOT}/db_shell.sh" << 'EOF'
#!/bin/bash
psql -U hartonomous -d hartonomous
EOF
chmod +x "${PROJECT_ROOT}/db_shell.sh"

# Reset database
cat > "${PROJECT_ROOT}/reset_db.sh" << 'EOF'
#!/bin/bash
set -e
echo "⚠️  This will DROP and recreate the database!"
read -p "Continue? (yes/no): " -r
if [ "$REPLY" = "yes" ]; then
    sudo -u postgres dropdb --if-exists hartonomous
    sudo -u postgres createdb -O hartonomous hartonomous
    ./scripts/init-database.sh
    echo "✅ Database reset complete"
fi
EOF
chmod +x "${PROJECT_ROOT}/reset_db.sh"

echo -e "${GREEN}✓${NC} Helper scripts created"

# ============================================================================
# Summary
# ============================================================================
echo -e "\n${BLUE}═══════════════════════════════════════════${NC}"
echo -e "${GREEN}✅ Local development environment ready!${NC}"
echo -e "${BLUE}═══════════════════════════════════════════${NC}"
echo -e ""
echo -e "${YELLOW}Quick Start:${NC}"
echo -e "  ${BLUE}./run_local_api.sh${NC}          # Start API server"
echo -e "  ${BLUE}./db_shell.sh${NC}              # Open database shell"
echo -e "  ${BLUE}./reset_db.sh${NC}              # Reset database"
echo -e ""
echo -e "${YELLOW}API Endpoints:${NC}"
echo -e "  ${BLUE}http://localhost:8000/docs${NC}  # API documentation"
echo -e "  ${BLUE}http://localhost:8000/v1/health${NC} # Health check"
echo -e ""
echo -e "${BLUE}═══════════════════════════════════════════${NC}"
