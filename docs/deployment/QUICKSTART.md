# Quick Start Deployment Guide

**Deploy Hartonomous API to Azure Arc-enabled machines in 10 minutes.**

---

## ?? **Prerequisites**

? Azure Arc-enabled machines (HART-DESKTOP, hart-server)  
? PostgreSQL 15+ installed  
? Azure Key Vault configured  
? Managed Identity permissions granted

---

## ?? **Step-by-Step Deployment**

### **Step 1: Clone Repository**

```bash
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous
```

---

### **Step 2: Configure Environment**

**Create `.env` file (NOT tracked in Git):**

```bash
# Copy template
cp .env.example .env
```

**For Local Development:**
```ini
USE_AZURE_CONFIG=false
PGHOST=localhost
PGUSER=hartonomous
PGPASSWORD=YOUR_LOCAL_PASSWORD
PGDATABASE=hartonomous
LOG_LEVEL=DEBUG
```

**For Production (Azure):**
```ini
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
LOG_LEVEL=INFO
```

?? **IMPORTANT:** Never commit `.env` to Git. Passwords must be in Azure Key Vault.

---

### **Step 3: Create PostgreSQL Database**

**On Windows (HART-DESKTOP):**
```powershell
# Connect as superuser
psql -U postgres

# Create user and database
CREATE USER hartonomous WITH PASSWORD 'SECURE_PASSWORD';
ALTER USER hartonomous CREATEDB;
CREATE DATABASE hartonomous OWNER hartonomous;
\q
```

**On Linux (hart-server):**
```bash
# Connect as superuser
sudo -u postgres psql

# Create user and database
CREATE USER ai_architect WITH PASSWORD 'SECURE_PASSWORD';
CREATE DATABASE hartonomous OWNER ai_architect;
\q
```

**Store password in Key Vault:**
```bash
az keyvault secret set \
  --vault-name kv-hartonomous \
  --name "PostgreSQL-MACHINE_NAME-PASSWORD" \
  --value "SECURE_PASSWORD"
```

---

### **Step 4: Load Database Schema**

```bash
cd schema

# Load extensions
psql -U postgres -d hartonomous -f extensions/001_postgis.sql
psql -U postgres -d hartonomous -f extensions/002_plpython.sql
psql -U postgres -d hartonomous -f extensions/003_pg_trgm.sql
psql -U postgres -d hartonomous -f extensions/004_btree_gin.sql

# Load types
for file in types/*.sql; do
  psql -U postgres -d hartonomous -f "$file"
done

# Load tables
for file in core/tables/*.sql; do
  psql -U postgres -d hartonomous -f "$file"
done

# Load functions
for dir in core/functions/*/; do
  for file in "$dir"*.sql; do
    psql -U postgres -d hartonomous -f "$file"
  done
done

# Load triggers
for file in core/triggers/*.sql; do
  psql -U postgres -d hartonomous -f "$file"
done
```

**Verify schema loaded:**
```bash
psql -U postgres -d hartonomous -c "\dt"
psql -U postgres -d hartonomous -c "SELECT COUNT(*) FROM pg_proc WHERE pronamespace = 'public'::regnamespace;"
```

---

### **Step 5: Grant Permissions**

```sql
-- Grant permissions to application user
GRANT CONNECT ON DATABASE hartonomous TO hartonomous; -- or ai_architect
GRANT USAGE ON SCHEMA public TO hartonomous;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO hartonomous;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO hartonomous;
GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public TO hartonomous;
```

---

### **Step 6: Install API Dependencies**

```bash
cd api

# Create virtual environment (recommended)
python -m venv venv

# Activate
# Windows:
.\venv\Scripts\activate
# Linux:
source venv/bin/activate

# Install dependencies
pip install -r requirements.txt
```

---

### **Step 7: Test Azure Configuration** (Production Only)

```bash
# Verify Managed Identity can access Key Vault
az keyvault secret list \
  --vault-name kv-hartonomous \
  --query "[].name" -o tsv

# Should list secrets without requiring login
```

---

### **Step 8: Start API**

```bash
cd api
python main.py
```

**Expected output:**
```
INFO:     Starting Hartonomous API v0.6.0...
INFO:     Creating connection pool (min=5, max=20)...
INFO:     Connection pool opened successfully
INFO:     AGE sync worker started
INFO:     ? Hartonomous API ready
INFO:     Uvicorn running on http://0.0.0.0:8000
```

---

### **Step 9: Verify Deployment**

```bash
# Health check
curl http://localhost:8000/v1/health

# Readiness (tests database)
curl http://localhost:8000/v1/ready

# Statistics
curl http://localhost:8000/v1/stats

# API documentation
open http://localhost:8000/docs
```

**Expected responses:**
```json
// /v1/health
{"status":"ok","service":"hartonomous-api","version":"0.6.0"}

// /v1/ready
{
  "status":"ready",
  "database":{"connected":true,"version":"PostgreSQL 16.11","tables":3}
}

// /v1/stats
{
  "status":"ok",
  "statistics":{"atoms":0,"compositions":0,"relations":0}
}
```

---

## ?? **Troubleshooting**

### **Database Connection Failed**

```bash
# Check PostgreSQL running
# Windows:
Get-Service postgresql*
# Linux:
systemctl status postgresql

# Test connection
psql -U hartonomous -d hartonomous -c "SELECT version();"
```

### **Azure Key Vault Access Denied**

```bash
# Verify Managed Identity
az connectedmachine show \
  --name MACHINE_NAME \
  --resource-group rg-hartonomous \
  --query identity.principalId -o tsv

# Check role assignments
az role assignment list \
  --scope /subscriptions/SUBSCRIPTION_ID/resourceGroups/rg-hartonomous/providers/Microsoft.KeyVault/vaults/kv-hartonomous
```

### **Missing Extensions**

```bash
# Check installed extensions
psql -U postgres -d hartonomous -c "SELECT * FROM pg_extension;"

# Install missing extension
psql -U postgres -d hartonomous -c "CREATE EXTENSION IF NOT EXISTS postgis;"
```

---

## ?? **Production Deployment**

### **Run as Service (Windows)**

```powershell
# Install NSSM
# https://nssm.cc/download

# Install service
nssm install HartonomousAPI `
  C:\Hartonomous\api\venv\Scripts\python.exe `
  C:\Hartonomous\api\main.py

# Configure
nssm set HartonomousAPI AppDirectory C:\Hartonomous\api

# Start
nssm start HartonomousAPI
```

### **Run as Service (Linux)**

```bash
# Create systemd service
sudo nano /etc/systemd/system/hartonomous-api.service
```

```ini
[Unit]
Description=Hartonomous API
After=network.target postgresql.service

[Service]
Type=simple
User=ahart
WorkingDirectory=/home/ahart/Hartonomous/api
Environment="PATH=/home/ahart/Hartonomous/api/venv/bin"
ExecStart=/home/ahart/Hartonomous/api/venv/bin/python main.py
Restart=always

[Install]
WantedBy=multi-user.target
```

```bash
# Enable and start
sudo systemctl daemon-reload
sudo systemctl enable hartonomous-api
sudo systemctl start hartonomous-api
sudo systemctl status hartonomous-api
```

---

## ?? **Next Steps**

- [Security & Credentials Guide](../security/CREDENTIALS.md)
- [API Documentation](../api-reference/rest-api.md)
- [Azure Production Deployment](./azure-production.md)

---

**Deployment Time:** ~10 minutes  
**Difficulty:** Intermediate  
**Prerequisites:** PostgreSQL, Python 3.10+, Azure CLI

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
