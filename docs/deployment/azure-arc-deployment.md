# Running Hartonomous on Azure Arc Machines

**Deployment guide for HART-DESKTOP and hart-server**

---

## ?? **Architecture**

```
Azure Arc Machine (HART-DESKTOP or hart-server)
    ? Managed Identity
    ?
Azure Key Vault (kv-hartonomous)
    ?? PostgreSQL-Hartonomous-Password
    ?? AzureAd-ClientSecret
    ?? EntraExternalId-ClientSecret
    ?
Azure App Configuration (appconfig-hartonomous)
    ?? Hartonomous:Api:* (API settings)
    ?? ConnectionStrings:PostgreSQL-* (connection strings)
    ?? References to Key Vault secrets
    ?
Hartonomous API (FastAPI)
    ?
Local PostgreSQL (Port 5432)
    ?? Database: hartonomous
```

---

## ? **Prerequisites Verified**

### **Azure Resources (Already Configured)**
- ? Key Vault: `kv-hartonomous`
- ? App Configuration: `appconfig-hartonomous`
- ? Arc Machines: `HART-DESKTOP`, `hart-server`
- ? Managed Identities with permissions
- ? PostgreSQL password in Key Vault
- ? Connection strings in App Configuration

### **Local Prerequisites**
- ? PostgreSQL installed on both machines
- ? Python 3.10+ installed
- ? Azure Arc agent connected
- ?? PostgreSQL database created (hartonomous)
- ?? Firewall rules (if needed for remote access)

---

## ?? **Deployment Steps**

### **Step 1: Create PostgreSQL Database**

**On HART-DESKTOP (Windows):**
```powershell
# Connect to PostgreSQL
psql -U postgres

# Create database
CREATE DATABASE hartonomous;

# Verify
\l
\q
```

**On hart-server (Linux):**
```bash
# Connect to PostgreSQL
sudo -u postgres psql

# Create database
CREATE DATABASE hartonomous;

# Verify
\l
\q
```

---

### **Step 2: Install Hartonomous API**

**Clone Repository:**
```bash
# HART-DESKTOP (PowerShell)
cd C:\
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous\api

# hart-server (bash)
cd /opt
sudo git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous/api
```

**Install Dependencies:**
```bash
# HART-DESKTOP
python -m venv venv
.\venv\Scripts\activate
pip install -r requirements.txt

# hart-server
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

---

### **Step 3: Configure for Azure**

**Create `.env` file:**

**HART-DESKTOP:**
```powershell
# api/.env
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
LOG_LEVEL=INFO
```

**hart-server:**
```bash
# api/.env
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
LOG_LEVEL=INFO
```

**That's it!** All other settings come from App Configuration.

---

### **Step 4: Test Azure Configuration**

**Verify Azure connectivity:**
```powershell
# Test Key Vault access
az keyvault secret show --vault-name kv-hartonomous --name PostgreSQL-Hartonomous-Password --query value -o tsv

# Test App Configuration access
az appconfig kv list --name appconfig-hartonomous --key "Hartonomous:*"
```

**Test Python Azure SDK:**
```python
# test_azure.py
from azure.identity import DefaultAzureCredential
from azure.keyvault.secrets import SecretClient

credential = DefaultAzureCredential()
client = SecretClient(
    vault_url="https://kv-hartonomous.vault.azure.net/",
    credential=credential
)

# This should work via Managed Identity
secret = client.get_secret("PostgreSQL-Hartonomous-Password")
print(f"Retrieved password: {secret.value[:5]}***")
```

Run test:
```bash
python test_azure.py
```

---

### **Step 5: Initialize Database Schema**

**Load PostgreSQL schema:**
```bash
# From Hartonomous root directory
cd schema

# Run initialization scripts
psql -U postgres -d hartonomous -f 01_extensions.sql
psql -U postgres -d hartonomous -f 02_core_schema.sql
psql -U postgres -d hartonomous -f 03_functions.sql
# ... continue with all schema files
```

---

### **Step 6: Start API**

**HART-DESKTOP (PowerShell):**
```powershell
cd api
.\venv\Scripts\activate

# Start API
python main.py
```

**hart-server (bash):**
```bash
cd api
source venv/bin/activate

# Start API
python main.py
```

**Verify:**
```bash
curl http://localhost:8000/v1/health
curl http://localhost:8000/v1/ready
```

---

### **Step 7: Run as Service**

**HART-DESKTOP (Windows Service):**

Create `hartonomous-api.xml` (NSSM config):
```xml
<service>
  <id>HartonomousAPI</id>
  <name>Hartonomous API</name>
  <description>Hartonomous REST API Service</description>
  <executable>C:\Hartonomous\api\venv\Scripts\python.exe</executable>
  <arguments>C:\Hartonomous\api\main.py</arguments>
  <workingdirectory>C:\Hartonomous\api</workingdirectory>
  <logmode>rotate</logmode>
</service>
```

Install service:
```powershell
# Download NSSM
# https://nssm.cc/download

# Install service
nssm install HartonomousAPI

# Configure
nssm set HartonomousAPI Application C:\Hartonomous\api\venv\Scripts\python.exe
nssm set HartonomousAPI AppParameters C:\Hartonomous\api\main.py
nssm set HartonomousAPI AppDirectory C:\Hartonomous\api

# Start
nssm start HartonomousAPI
```

**hart-server (systemd):**

Create `/etc/systemd/system/hartonomous-api.service`:
```ini
[Unit]
Description=Hartonomous API
After=network.target postgresql.service

[Service]
Type=simple
User=hartonomous
WorkingDirectory=/opt/Hartonomous/api
Environment="PATH=/opt/Hartonomous/api/venv/bin"
ExecStart=/opt/Hartonomous/api/venv/bin/python main.py
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable hartonomous-api
sudo systemctl start hartonomous-api
sudo systemctl status hartonomous-api
```

---

## ?? **Security Configuration**

### **Firewall Rules**

**HART-DESKTOP (Windows Firewall):**
```powershell
# Allow API port
New-NetFirewallRule -DisplayName "Hartonomous API" `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 8000 `
  -Action Allow
```

**hart-server (UFW):**
```bash
sudo ufw allow 8000/tcp
sudo ufw status
```

---

## ?? **Monitoring**

### **Health Checks**

```bash
# API health
curl http://localhost:8000/v1/health

# Database connectivity
curl http://localhost:8000/v1/ready

# Statistics
curl http://localhost:8000/v1/stats
```

### **Logs**

**HART-DESKTOP:**
```powershell
# View service logs
nssm rotate HartonomousAPI
Get-Content C:\Hartonomous\api\logs\api.log -Tail 100 -Wait
```

**hart-server:**
```bash
# View service logs
sudo journalctl -u hartonomous-api -f

# Or application logs
tail -f /opt/Hartonomous/api/logs/api.log
```

---

## ?? **Troubleshooting**

### **Azure Authentication Issues**

**Test Managed Identity:**
```bash
# Get access token
az account get-access-token --resource https://vault.azure.net

# Should work without login (uses Arc Managed Identity)
```

**Check Arc agent:**
```powershell
# HART-DESKTOP
azcmagent show

# hart-server
sudo azcmagent show
```

### **Database Connection Issues**

**Test PostgreSQL:**
```bash
psql -U postgres -d hartonomous -c "SELECT version();"
```

**Check password:**
```bash
az keyvault secret show --vault-name kv-hartonomous --name PostgreSQL-Hartonomous-Password --query value -o tsv
```

### **App Configuration Issues**

**Verify settings:**
```bash
az appconfig kv list --name appconfig-hartonomous --key "Hartonomous:*"
```

---

## ?? **Documentation**

- [API Documentation](../api/README.md)
- [Schema Documentation](../schema/README.md)
- [Azure Production Deployment](./azure-production.md)

---

**Quick Links:**
- Health: http://localhost:8000/v1/health
- Docs: http://localhost:8000/docs
- ReDoc: http://localhost:8000/redoc

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
