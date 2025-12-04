# Hartonomous Production Deployment Guide for HART-SERVER

## ?? Quick Start

This guide will walk you through deploying Hartonomous to your production Linux server (HART-SERVER).

## ?? Prerequisites

- **HART-SERVER**: Ubuntu 22.04 LTS or Debian 12+ (Linux)
- **Root access**: `sudo` privileges required
- **.NET 10 Runtime**: Will be installed by script
- **PostgreSQL**: Database server (local or remote)
- **Redis**: Cache server (local or remote)
- **Domain names**: Configured DNS records pointing to server
- **SSL Certificates**: Let's Encrypt recommended

---

## ?? Step 1: Initial Server Setup

### Run the automated setup script:

```bash
# Copy script to server
scp deploy/production-setup-linux.sh root@hart-server:/tmp/

# SSH to server
ssh root@hart-server

# Make executable and run
chmod +x /tmp/production-setup-linux.sh
/tmp/production-setup-linux.sh
```

This script will:
- ? Install .NET 10 Runtime
- ? Create application user (`hartonomous`)
- ? Set up directory structure
- ? Configure systemd services
- ? Set up Nginx reverse proxy
- ? Configure firewall (UFW)
- ? Install monitoring tools
- ? Set up log rotation

---

## ??? Step 2: Database Setup

### PostgreSQL with PostGIS

```bash
# Install PostgreSQL and PostGIS
sudo apt-get install postgresql postgresql-contrib postgis postgresql-15-postgis-3

# Switch to postgres user
sudo -i -u postgres

# Create database and user
createuser hartonomous
createdb hartonomous_prod -O hartonomous
psql -c "ALTER USER hartonomous WITH PASSWORD 'SECURE_PASSWORD_HERE';"

# Enable PostGIS extension
psql -d hartonomous_prod -c "CREATE EXTENSION postgis;"
psql -d hartonomous_prod -c "CREATE EXTENSION postgis_topology;"

# Exit postgres user
exit
```

### Redis Cache

```bash
# Install Redis
sudo apt-get install redis-server

# Configure Redis to listen only on localhost
sudo nano /etc/redis/redis.conf
# Find and set: bind 127.0.0.1 ::1

# Restart Redis
sudo systemctl restart redis-server
sudo systemctl enable redis-server
```

---

## ?? Step 3: Security Configuration

### Azure Key Vault Setup

```bash
# Install Azure CLI
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# Login
az login

# Create or configure Key Vault access
# The application uses Managed Identity in Azure
# For on-prem, use Service Principal

# Create service principal
az ad sp create-for-rbac --name hartonomous-prod \\
    --role "Key Vault Secrets User" \\
    --scopes /subscriptions/{subscription-id}/resourceGroups/{rg-name}/providers/Microsoft.KeyVault/vaults/hartonomous-kv
```

### Store credentials in Key Vault:

```bash
az keyvault secret set --vault-name hartonomous-kv --name "DbPassword" --value "YOUR_DB_PASSWORD"
az keyvault secret set --vault-name hartonomous-kv --name "RedisPassword" --value "YOUR_REDIS_PASSWORD"
```

---

## ?? Step 4: SSL Certificates

### Using Let's Encrypt (Recommended)

```bash
# Install Certbot
sudo apt-get install certbot python3-certbot-nginx

# Obtain certificates
sudo certbot --nginx -d api.hartonomous.com -d app.hartonomous.com

# Certbot will automatically configure Nginx
# Certificates auto-renew via cron job
```

---

## ?? Step 5: Build and Deploy Application

### On your development machine:

```bash
# Navigate to solution directory
cd D:\\Repositories\\Hartonomous

# Clean and build for production
dotnet clean
dotnet publish Hartonomous.API/Hartonomous.API.csproj -c Release -o ./publish/api
dotnet publish Hartonomous.Worker/Hartonomous.Worker.csproj -c Release -o ./publish/worker
dotnet publish Hartonomous.App/Hartonomous.App.Web/Hartonomous.App.Web.csproj -c Release -o ./publish/web
```

### Deploy to server:

```bash
# Using the deployment script
chmod +x deploy/deploy-app.sh
./deploy/deploy-app.sh all

# Or manually:
scp -r ./publish/api/* hartonomous@hart-server:/opt/hartonomous/api/
scp -r ./publish/worker/* hartonomous@hart-server:/opt/hartonomous/worker/
scp -r ./publish/web/* hartonomous@hart-server:/opt/hartonomous/web/
```

---

## ?? Step 6: Configure Application Settings

### Edit production settings on server:

```bash
# API configuration
sudo nano /opt/hartonomous/api/appsettings.Production.json

# Worker configuration
sudo nano /opt/hartonomous/worker/appsettings.Production.json
```

### Required settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=hartonomous_prod;Username=hartonomous;Password=YOUR_PASSWORD",
    "Redis": "localhost:6379"
  },
  "Authentication": {
    "TenantId": "YOUR_TENANT_ID",
    "ClientId": "YOUR_CLIENT_ID",
    "Authority": "https://login.microsoftonline.com/YOUR_TENANT_ID"
  },
  "KeyVault": {
    "Uri": "https://hartonomous-kv.vault.azure.net/"
  }
}
```

---

## ?? Step 7: Start Services

```bash
# Start services
sudo systemctl start hartonomous-api
sudo systemctl start hartonomous-worker
sudo systemctl restart nginx

# Enable services to start on boot
sudo systemctl enable hartonomous-api
sudo systemctl enable hartonomous-worker
sudo systemctl enable nginx

# Check status
sudo systemctl status hartonomous-api
sudo systemctl status hartonomous-worker
sudo systemctl status nginx
```

---

## ?? Step 8: Verify Deployment

### Health Checks

```bash
# API health
curl https://api.hartonomous.com/health

# Liveness
curl https://api.hartonomous.com/health/live

# Readiness
curl https://api.hartonomous.com/health/ready

# Web app
curl https://app.hartonomous.com
```

### View Logs

```bash
# API logs
sudo journalctl -u hartonomous-api -f

# Worker logs
sudo journalctl -u hartonomous-worker -f

# Nginx logs
sudo tail -f /var/log/nginx/hartonomous-api-access.log
sudo tail -f /var/log/nginx/hartonomous-api-error.log
```

---

## ?? Step 9: Monitoring Setup

### Prometheus & Grafana (Optional)

```bash
# Install Prometheus
wget https://github.com/prometheus/prometheus/releases/latest/download/prometheus-*.linux-amd64.tar.gz
tar xvfz prometheus-*.linux-amd64.tar.gz
sudo mv prometheus-*/prometheus /usr/local/bin/
sudo mv prometheus-*/promtool /usr/local/bin/

# Configure Prometheus to scrape endpoints
sudo nano /etc/prometheus/prometheus.yml
```

**Prometheus config:**
```yaml
scrape_configs:
  - job_name: 'hartonomous-api'
    static_configs:
      - targets: ['localhost:5000']
    metrics_path: '/metrics'
  
  - job_name: 'node-exporter'
    static_configs:
      - targets: ['localhost:9100']
```

---

## ?? Step 10: Database Migrations

```bash
# SSH to server
ssh hartonomous@hart-server

# Navigate to API directory
cd /opt/hartonomous/api

# Run EF Core migrations
dotnet ef database update --connection "Host=localhost;Database=hartonomous_prod;Username=hartonomous;Password=YOUR_PASSWORD"
```

---

## ??? Security Hardening Checklist

- [x] **Firewall configured** (UFW blocking all except 22, 80, 443)
- [x] **systemd security features** enabled (sandboxing, capabilities)
- [x] **Fail2ban** installed for brute-force protection
- [x] **Rate limiting** enabled in Nginx and application
- [x] **SSL/TLS** with strong ciphers (TLS 1.2/1.3 only)
- [x] **Security headers** (HSTS, CSP, X-Frame-Options, etc.)
- [x] **Secrets in Key Vault** (no credentials in config files)
- [x] **Least privilege** (dedicated user with minimal permissions)
- [x] **Log rotation** configured
- [x] **Automatic security updates** enabled

### Enable automatic security updates:

```bash
sudo apt-get install unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
```

### Install Fail2ban:

```bash
sudo apt-get install fail2ban
sudo systemctl enable fail2ban
sudo systemctl start fail2ban
```

---

## ?? Maintenance Tasks

### Update Application

```bash
# 1. Build new version locally
dotnet publish -c Release

# 2. Deploy using script
./deploy/deploy-app.sh all

# Services are automatically restarted
```

### Backup Database

```bash
# Create backup
sudo -u postgres pg_dump hartonomous_prod > backup_$(date +%Y%m%d).sql

# Restore from backup
sudo -u postgres psql hartonomous_prod < backup_20251204.sql
```

### View Service Status

```bash
# All hartonomous services
sudo systemctl status hartonomous-*

# Resource usage
sudo systemctl status hartonomous-api --no-pager
```

### Restart Services

```bash
sudo systemctl restart hartonomous-api
sudo systemctl restart hartonomous-worker
sudo systemctl reload nginx
```

---

## ?? Troubleshooting

### Service won't start

```bash
# Check logs
sudo journalctl -u hartonomous-api -n 100 --no-pager

# Check permissions
ls -la /opt/hartonomous/api

# Check configuration
sudo -u hartonomous dotnet /opt/hartonomous/api/Hartonomous.API.dll --environment Production
```

### Database connection issues

```bash
# Test connection
psql -h localhost -U hartonomous -d hartonomous_prod

# Check PostgreSQL is running
sudo systemctl status postgresql

# Check firewall
sudo ufw status
```

### High memory usage

```bash
# Check memory
free -h

# Restart services
sudo systemctl restart hartonomous-*

# Check for memory leaks
dotnet-dump collect -p $(pgrep -f Hartonomous.API.dll)
```

---

## ?? Support

- **Documentation**: `/docs` folder in repository
- **Logs**: `/var/log/hartonomous/`
- **Health**: `https://api.hartonomous.com/health`
- **Metrics**: `https://api.hartonomous.com/metrics` (internal only)

---

## ? Post-Deployment Checklist

- [ ] All services running
- [ ] Health checks passing
- [ ] SSL certificates valid
- [ ] Database migrations applied
- [ ] Monitoring configured
- [ ] Backups scheduled
- [ ] Security headers present
- [ ] Rate limiting working
- [ ] Logs rotating
- [ ] Firewall configured

**Congratulations! Hartonomous is now running in production on HART-SERVER! ??**
