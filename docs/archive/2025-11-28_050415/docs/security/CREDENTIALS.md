# Security & Credentials Management

**DO NOT commit sensitive information to Git.**

---

## ?? **SECRET STORAGE**

### **Azure Key Vault (Production)**

All production secrets are stored in Azure Key Vault: `kv-hartonomous`

**Secrets stored:**
- PostgreSQL passwords (per machine)
- Entra ID client secrets
- CIAM client secrets
- API keys (HuggingFace, Stripe, etc.)

**Access via:**
- Azure Arc Managed Identity (HART-DESKTOP, hart-server)
- User accounts with RBAC roles

---

## ?? **WHAT NEVER GOES IN GIT**

### **Files (in .gitignore)**
```
.env
.env.local
*.env
*.key
*.pem
*.pfx
secrets/
credentials/
```

### **Information**
- ? Passwords
- ? API keys
- ? Connection strings with credentials
- ? Certificate private keys
- ? SSH private keys
- ? JWT secrets

---

## ? **WHAT GOES IN GIT**

### **Configuration Templates**
```
.env.example          # Placeholder values only
api/.env.example      # Placeholder values only
```

### **Documentation**
```
? Architecture diagrams
? Deployment steps (without credentials)
? Configuration instructions (reference Key Vault)
? Example queries
```

---

## ?? **CREDENTIAL SETUP GUIDE**

### **For Local Development (HART-DESKTOP)**

**1. Create `.env` file (NOT tracked in Git):**

```bash
# Copy template
cp .env.example .env

# Edit with your local credentials
# DO NOT use production passwords in local .env
```

**2. Example local `.env`:**
```ini
USE_AZURE_CONFIG=false
PGHOST=localhost
PGPORT=5432
PGUSER=hartonomous
PGPASSWORD=YOUR_LOCAL_PASSWORD_HERE
PGDATABASE=hartonomous
LOG_LEVEL=DEBUG
```

**3. Never commit `.env`:**
```bash
# Verify it's ignored
git status .env
# Should show: "No such file or directory" (not tracked)
```

---

### **For Production (Azure Arc)**

**1. Create `.env` with Azure references:**
```ini
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
LOG_LEVEL=INFO
```

**2. Passwords retrieved automatically from Key Vault via Managed Identity**

**3. No passwords in `.env` file**

---

## ?? **KEY VAULT SETUP**

### **Store a Secret**

```bash
# Generic command (replace values)
az keyvault secret set \
  --vault-name kv-hartonomous \
  --name "Secret-Name" \
  --value "SECRET_VALUE_HERE" \
  --description "Description of secret"
```

### **Retrieve a Secret (for verification only)**

```bash
# NEVER log this output to files or commits
az keyvault secret show \
  --vault-name kv-hartonomous \
  --name "Secret-Name" \
  --query value -o tsv
```

---

## ??? **APP CONFIGURATION SETUP**

### **Store Non-Secret Configuration**

```bash
# Public configuration values
az appconfig kv set \
  --name appconfig-hartonomous \
  --key "Hartonomous:Api:Host" \
  --value "0.0.0.0" \
  --yes
```

### **Reference Key Vault Secret**

```bash
# Reference a secret (doesn't expose value)
az appconfig kv set-keyvault \
  --name appconfig-hartonomous \
  --key "ConnectionStrings:PostgreSQL-Password" \
  --secret-identifier "https://kv-hartonomous.vault.azure.net/secrets/PostgreSQL-Password" \
  --yes
```

---

## ?? **POSTGRESQL USER SETUP**

### **HART-DESKTOP (Local Development)**

```sql
-- Connect as postgres superuser
psql -U postgres

-- Create non-privileged user for API
CREATE USER hartonomous WITH PASSWORD 'YOUR_LOCAL_PASSWORD';
ALTER USER hartonomous CREATEDB;
CREATE DATABASE hartonomous OWNER hartonomous;

-- Grant permissions
GRANT CONNECT ON DATABASE hartonomous TO hartonomous;
GRANT ALL PRIVILEGES ON DATABASE hartonomous TO hartonomous;
```

**Store password in local `.env` (NOT Git)**

---

### **hart-server (Production)**

```sql
-- Connect as postgres superuser
sudo -u postgres psql

-- Secure superuser
ALTER USER postgres WITH PASSWORD 'SECURE_PASSWORD_FROM_KEY_VAULT';

-- Create application user
CREATE USER ai_architect WITH PASSWORD 'SECURE_PASSWORD_FROM_KEY_VAULT';

-- Grant permissions (after schema loaded)
GRANT CONNECT ON DATABASE hartonomous TO ai_architect;
GRANT USAGE ON SCHEMA public TO ai_architect;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO ai_architect;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO ai_architect;
GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public TO ai_architect;
```

**Store passwords in Azure Key Vault:**
```bash
az keyvault secret set \
  --vault-name kv-hartonomous \
  --name "PostgreSQL-hart-server-postgres-Password" \
  --value "SECURE_PASSWORD"

az keyvault secret set \
  --vault-name kv-hartonomous \
  --name "PostgreSQL-hart-server-ai-architect-Password" \
  --value "SECURE_PASSWORD"
```

---

## ??? **MANAGED IDENTITY SETUP**

### **Azure Arc Machines**

Both `HART-DESKTOP` and `hart-server` have system-assigned Managed Identities:

```bash
# View identity
az connectedmachine show \
  --name HART-DESKTOP \
  --resource-group rg-hartonomous \
  --query identity
```

### **Grant Key Vault Access**

```bash
# Get identity principal ID
PRINCIPAL_ID=$(az connectedmachine show \
  --name HART-DESKTOP \
  --resource-group rg-hartonomous \
  --query identity.principalId -o tsv)

# Grant Key Vault Secrets User role
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/{subscription-id}/resourceGroups/rg-hartonomous/providers/Microsoft.KeyVault/vaults/kv-hartonomous
```

### **Grant App Configuration Access**

```bash
# Grant App Configuration Data Reader role
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "App Configuration Data Reader" \
  --scope /subscriptions/{subscription-id}/resourceGroups/rg-hartonomous/providers/Microsoft.AppConfiguration/configurationStores/appconfig-hartonomous
```

---

## ?? **DEPLOYMENT WORKFLOW**

### **1. Developer Setup (Local)**

```bash
# Clone repository
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous

# Create local .env (NOT in Git)
cp .env.example .env
# Edit .env with local PostgreSQL credentials

# Create PostgreSQL database
psql -U postgres -c "CREATE USER hartonomous WITH PASSWORD 'local_dev_password';"
psql -U postgres -c "CREATE DATABASE hartonomous OWNER hartonomous;"

# Load schema
cd schema
for file in extensions/*.sql; do psql -U postgres -d hartonomous -f $file; done
for file in core/tables/*.sql; do psql -U postgres -d hartonomous -f $file; done
# ... continue with all schema files

# Install Python dependencies
cd ../api
pip install -r requirements.txt

# Start API
python main.py
```

---

### **2. Production Deployment (Azure Arc)**

```bash
# SSH to production server
ssh ahart@192.168.1.2

# Clone repository
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous

# Create .env with Azure config (NO PASSWORDS)
cat > .env << 'EOF'
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
LOG_LEVEL=INFO
EOF

# Verify Managed Identity can access Key Vault
az keyvault secret show \
  --vault-name kv-hartonomous \
  --name PostgreSQL-hart-server-ai-architect-Password \
  --query name -o tsv
# Should succeed without login (uses Managed Identity)

# Create database and user
sudo -u postgres psql << 'EOF'
CREATE USER ai_architect WITH PASSWORD 'GET_FROM_KEY_VAULT';
CREATE DATABASE hartonomous OWNER ai_architect;
EOF

# Load schema (same as local)

# Install Python dependencies
cd api
pip install -r requirements.txt

# Start API (automatically uses Azure Key Vault)
python main.py
```

---

## ?? **SECURITY CHECKLIST**

### **Before Every Commit**

- [ ] No passwords in code
- [ ] No API keys in code
- [ ] `.env` is in `.gitignore`
- [ ] Only `.env.example` with placeholders committed
- [ ] No connection strings with embedded credentials
- [ ] No Azure subscription IDs in public docs (use placeholders)
- [ ] No Key Vault secret values in commit messages

### **Before Every Deployment**

- [ ] Passwords stored in Key Vault
- [ ] Managed Identity has required permissions
- [ ] `.env` file created with Azure references (no secrets)
- [ ] PostgreSQL users created with strong passwords
- [ ] Firewall rules configured appropriately
- [ ] SSH keys (not passwords) for remote access

### **Regular Maintenance**

- [ ] Rotate passwords quarterly
- [ ] Review Key Vault access logs
- [ ] Audit role assignments
- [ ] Update `.env.example` if configuration changes
- [ ] Document new secrets in this file (not values, just names)

---

## ?? **AUDIT COMMANDS**

### **Check for Leaked Secrets in Git**

```bash
# Search Git history for potential secrets
git log --all --full-history --source --date=local -S "password" -- .env

# Should return nothing if .env never committed
```

### **Verify .gitignore Working**

```bash
# Check .env is ignored
git check-ignore .env
# Should output: .env

# Check status
git status --ignored .env
# Should show as ignored
```

### **List Key Vault Secrets (Names Only)**

```bash
# Safe to document - no values
az keyvault secret list \
  --vault-name kv-hartonomous \
  --query "[].name" -o tsv
```

---

## ?? **REFERENCES**

- [Azure Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
- [Managed Identity Overview](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/overview)
- [Azure App Configuration](https://learn.microsoft.com/en-us/azure/azure-app-configuration/)
- [PostgreSQL Security](https://www.postgresql.org/docs/current/auth-pg-hba-conf.html)

---

## ?? **IF CREDENTIALS ARE LEAKED**

### **Immediate Actions**

1. **Rotate compromised credentials immediately**
   ```bash
   az keyvault secret set --vault-name kv-hartonomous --name SECRET_NAME --value NEW_VALUE
   ```

2. **Update PostgreSQL passwords**
   ```sql
   ALTER USER username WITH PASSWORD 'NEW_PASSWORD';
   ```

3. **Review access logs**
   ```bash
   az monitor activity-log list --resource-group rg-hartonomous
   ```

4. **If in Git history, rewrite history (DANGEROUS)**
   ```bash
   # Only if absolutely necessary
   git filter-branch --force --index-filter \
     "git rm --cached --ignore-unmatch .env" \
     --prune-empty --tag-name-filter cat -- --all
   ```

---

**Last Updated:** 2025-11-25  
**Maintained By:** AI Architect Team  
**Classification:** Internal Use Only

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
