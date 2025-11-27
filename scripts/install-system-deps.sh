#!/bin/bash
# ============================================================================
# System Dependencies Installation
# Must be run with sudo
# ============================================================================

set -e

if [ "$EUID" -ne 0 ]; then 
    echo "❌ Please run with sudo"
    exit 1
fi

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}📦 Installing system dependencies...${NC}"

# ============================================================================
# PostgreSQL with PostGIS
# ============================================================================
echo -e "\n${YELLOW}🗄️  PostgreSQL + PostGIS${NC}"

if ! command -v psql &> /dev/null; then
    echo -e "${BLUE}Adding PostgreSQL repository...${NC}"
    apt-get install -y wget ca-certificates
    sh -c 'echo "deb https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
    wget --quiet -O - https://www.postgresql.org/media/keys/ACCC4CF8.asc | apt-key add -
    apt-get update
    
    echo -e "${BLUE}Installing PostgreSQL 16 + PostGIS...${NC}"
    apt-get install -y \
        postgresql-16 \
        postgresql-16-postgis-3 \
        postgresql-plpython3-16 \
        postgresql-contrib-16
    
    systemctl enable postgresql
    systemctl start postgresql
    echo -e "${GREEN}✓${NC} PostgreSQL installed"
else
    echo -e "${GREEN}✓${NC} PostgreSQL already installed"
fi

# ============================================================================
# Python 3.13
# ============================================================================
echo -e "\n${YELLOW}🐍 Python 3.13${NC}"

if ! command -v python3.13 &> /dev/null; then
    echo -e "${BLUE}Adding deadsnakes PPA...${NC}"
    apt-get install -y software-properties-common
    add-apt-repository -y ppa:deadsnakes/ppa
    apt-get update
    
    echo -e "${BLUE}Installing Python 3.13...${NC}"
    apt-get install -y \
        python3.13 \
        python3.13-dev \
        python3.13-venv \
        python3.13-gdbm \
        python3.13-tk
    
    update-alternatives --install /usr/bin/python3 python3 /usr/bin/python3.13 1
    
    # Install pip
    python3.13 -m ensurepip --upgrade
    python3.13 -m pip install --upgrade pip setuptools wheel
    
    echo -e "${GREEN}✓${NC} Python 3.13 installed"
else
    echo -e "${GREEN}✓${NC} Python 3.13 already installed"
fi

# ============================================================================
# Docker
# ============================================================================
echo -e "\n${YELLOW}🐳 Docker${NC}"

if ! command -v docker &> /dev/null; then
    echo -e "${BLUE}Installing Docker...${NC}"
    apt-get install -y \
        ca-certificates \
        curl \
        gnupg \
        lsb-release
    
    mkdir -p /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    
    echo \
      "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
      $(lsb_release -cs) stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null
    
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
    
    systemctl enable docker
    systemctl start docker
    
    echo -e "${GREEN}✓${NC} Docker installed"
else
    echo -e "${GREEN}✓${NC} Docker already installed"
fi

# ============================================================================
# Development Tools
# ============================================================================
echo -e "\n${YELLOW}🛠️  Development Tools${NC}"

apt-get install -y \
    git \
    curl \
    wget \
    build-essential \
    libpq-dev \
    libbz2-dev \
    libreadline-dev \
    libsqlite3-dev \
    libssl-dev \
    zlib1g-dev \
    libffi-dev \
    liblzma-dev

echo -e "${GREEN}✓${NC} Development tools installed"

# ============================================================================
# GPU Support (Optional)
# ============================================================================
if lspci | grep -i nvidia &> /dev/null; then
    echo -e "\n${YELLOW}🎮 NVIDIA GPU detected${NC}"
    echo -e "${BLUE}CUDA toolkit should be installed separately if needed${NC}"
    echo -e "Visit: https://developer.nvidia.com/cuda-downloads"
fi

# ============================================================================
# Summary
# ============================================================================
echo -e "\n${BLUE}═══════════════════════════════════════════${NC}"
echo -e "${GREEN}✅ System dependencies installed!${NC}"
echo -e "${BLUE}═══════════════════════════════════════════${NC}"
echo -e ""
echo -e "${YELLOW}Next steps:${NC}"
echo -e "  1. Add user to docker group:"
echo -e "     ${BLUE}sudo usermod -aG docker \$USER${NC}"
echo -e "  2. Logout and login again (or run: newgrp docker)"
echo -e "  3. Run setup script:"
echo -e "     ${BLUE}./scripts/setup-local-dev.sh${NC}"
echo -e ""
