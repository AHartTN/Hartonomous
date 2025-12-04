# ?? Hartonomous HART-SERVER Multi-Environment Deployment

## ? Complete Setup Summary

Your Hartonomous solution is now configured for **production-grade multi-environment deployment** to HART-SERVER with:
- ? Development environment
- ? Staging environment  
- ? Production environment

---

## ?? Server Directory Structure

```
HART-SERVER (/srv/www/)
??? production/           # Port 5000
?   ??? api/
?   ??? worker/
?   ??? web/
??? staging/              # Port 5001
?   ??? api/
?   ??? worker/
?   ??? web/
??? development/          # Port 5002
    ??? api/
    ??? worker/
    ??? web/
```

---

## ?? Deployment Commands

### 1. Initial Server Setup (run once per environment)

```bash
# Setup production environment
sudo ./deploy/production-setup-linux.sh production

# Setup staging environment  
sudo ./deploy/production-setup-linux.sh staging

# Setup development environment
sudo ./deploy/production-setup-linux.sh development
```

**What this does:**
- ? Installs .NET 10 Runtime
- ? Creates `/srv/www/{environment}` directories
- ? Configures systemd services with security hardening
- ? Sets up Nginx reverse proxy with environment-specific config
- ? Configures firewall (UFW)
- ? Sets up log rotation
- ? Installs monitoring tools

### 2. Deploy Application (from your dev machine)

```bash
# Deploy all components to production
./deploy/deploy-app.sh production all

# Deploy only API to staging
./deploy/deploy-app.sh staging api

# Deploy specific component to development
./deploy/deploy-app.sh development worker
```

**What this does:**
- ? Builds application with correct configuration
- ? Stops existing services
- ? Backs up current deployment
- ? Copies new files to server
- ? Sets proper permissions
- ? Restarts services
- ? Verifies health

---

## ?? Domain/URL Configuration

| Environment | API Endpoint | Web App | Port |
|------------|-------------|---------|------|
| **Production** | https://api.hartonomous.com | https://app.hartonomous.com | 5000 |
| **Staging** | https://api-staging.hartonomous.com | https://app-staging.hartonomous.com | 5001 |
| **Development** | https://api-dev.hartonomous.com | https://app-dev.hartonomous.com | 5002 |

---

## ?? SSL Certificate Setup

```bash
# Production certificates
sudo certbot --nginx -d api.hartonomous.com -d app.hartonomous.com

# Staging certificates  
sudo certbot --nginx -d api-staging.hartonomous.com -d app-staging.hartonomous.com

# Development certificates
sudo certbot --nginx -d api-dev.hartonomous.com -d app-dev.hartonomous.com
```

---

## ?? Service Management

### Production Services
```bash
sudo systemctl start hartonomous-api-production
sudo systemctl start hartonomous-worker-production
sudo systemctl enable hartonomous-api-production
sudo systemctl enable hartonomous-worker-production

# View logs
sudo journalctl -u hartonomous-api-production -f
```

### Staging Services
```bash
sudo systemctl start hartonomous-api-staging
sudo systemctl start hartonomous-worker-staging
sudo systemctl enable hartonomous-api-staging
sudo systemctl enable hartonomous-worker-staging

# View logs
sudo journalctl -u hartonomous-api-staging -f
```

### Development Services
```bash
sudo systemctl start hartonomous-api-development
sudo systemctl start hartonomous-worker-development
sudo systemctl enable hartonomous-api-development
sudo systemctl enable hartonomous-worker-development

# View logs
sudo journalctl -u hartonomous-api-development -f
```

---

## ??? Database Configuration

```bash
# Connect to PostgreSQL
sudo -u postgres psql

# Create databases for each environment
CREATE DATABASE hartonomous_prod OWNER hartonomous;
CREATE DATABASE hartonomous_staging OWNER hartonomous;
CREATE DATABASE hartonomous_dev OWNER hartonomous;

# Enable PostGIS for each
\c hartonomous_prod
CREATE EXTENSION postgis;
CREATE EXTENSION postgis_topology;

\c hartonomous_staging
CREATE EXTENSION postgis;
CREATE EXTENSION postgis_topology;

\c hartonomous_dev
CREATE EXTENSION postgis;
CREATE EXTENSION postgis_topology;
```

---

## ?? Configuration Files

### appsettings.json Hierarchy

Each environment loads settings in this order:
1. `appsettings.json` (base)
2. `appsettings.{Environment}.json` (environment-specific)
3. Environment variables
4. Azure Key Vault secrets

### Environment-Specific Files Created:
- ? `Hartonomous.API/appsettings.Production.json`
- ? `Hartonomous.API/appsettings.Staging.json`
- ? `Hartonomous.API/appsettings.Development.json` (existing)

---

## ?? Environment Comparison

| Feature | Production | Staging | Development |
|---------|-----------|---------|-------------|
| **Port** | 5000 | 5001 | 5002 |
| **Database** | hartonomous_prod | hartonomous_staging | hartonomous_dev |
| **Rate Limit** | 1000/min | 5000/min | 10000/min |
| **Max Connections** | 1000 | 500 | 100 |
| **Log Level** | Information | Information | Debug |
| **Debug Mode** | ? | ? | ? |
| **SSL Required** | ? | ? | Optional |
| **Metrics Public** | ? | ? | ? |

---

## ?? Typical Deployment Workflow

```bash
# 1. Develop locally
git add .
git commit -m "New feature"
git push

# 2. Deploy to development
./deploy/deploy-app.sh development all

# 3. Test in dev
curl https://api-dev.hartonomous.com/health

# 4. Deploy to staging for QA
./deploy/deploy-app.sh staging all

# 5. After approval, deploy to production
./deploy/deploy-app.sh production all

# 6. Verify production health
curl https://api.hartonomous.com/health
```

---

## ?? Health Check Endpoints

```bash
# Production
curl https://api.hartonomous.com/health
curl https://api.hartonomous.com/health/live
curl https://api.hartonomous.com/health/ready

# Staging
curl https://api-staging.hartonomous.com/health

# Development
curl https://api-dev.hartonomous.com/health
```

---

## ??? Security Features

### Implemented in All Environments:
- ? **Zero Trust Authentication** (Microsoft Entra ID + JWT)
- ? **Rate Limiting** (multiple strategies)
- ? **CORS Policies** (environment-specific origins)
- ? **Security Headers** (HSTS, CSP, X-Frame-Options, etc.)
- ? **systemd Sandboxing** (restricted file access, no new privileges)
- ? **Firewall Rules** (UFW blocking all except 22, 80, 443)
- ? **SSL/TLS** (Let's Encrypt certificates)
- ? **Secrets Management** (Azure Key Vault)

### systemd Security Hardening:
- `NoNewPrivileges=true`
- `PrivateTmp=true`
- `ProtectSystem=strict`
- `ProtectHome=true`
- `ProtectKernelTunables=true`
- `RestrictRealtime=true`
- `MemoryDenyWriteExecute=true`

---

## ?? Log Files

```bash
# Application logs (systemd)
/var/log/hartonomous/production/
/var/log/hartonomous/staging/
/var/log/hartonomous/development/

# Nginx logs
/var/log/nginx/hartonomous-production-api-access.log
/var/log/nginx/hartonomous-staging-api-access.log
/var/log/nginx/hartonomous-development-api-access.log
```

---

## ?? Quick Troubleshooting

```bash
# Check service status
sudo systemctl status hartonomous-api-{environment}

# View recent logs (last 100 lines)
sudo journalctl -u hartonomous-api-{environment} -n 100

# Follow logs in real-time
sudo journalctl -u hartonomous-api-{environment} -f

# Restart service
sudo systemctl restart hartonomous-api-{environment}

# Check Nginx config
sudo nginx -t

# Reload Nginx
sudo systemctl reload nginx

# Check which ports are listening
sudo netstat -tlnp | grep -E '5000|5001|5002'

# Test database connection
psql -h localhost -U hartonomous -d hartonomous_{env}
```

---

## ?? Documentation Reference

- **Comprehensive Guide**: [DEPLOYMENT-GUIDE-LINUX.md](./DEPLOYMENT-GUIDE-LINUX.md)
- **Quick Reference**: [DEPLOYMENT-QUICK-REFERENCE.md](./DEPLOYMENT-QUICK-REFERENCE.md)
- **All Endpoints**: [DEPLOYMENT-ENDPOINTS.md](./DEPLOYMENT-ENDPOINTS.md)

---

## ?? Next Steps

### 1. Setup HART-SERVER (one-time)
```bash
# Copy script to server
scp deploy/production-setup-linux.sh root@hart-server:/tmp/

# SSH and run for each environment
ssh root@hart-server
chmod +x /tmp/production-setup-linux.sh

# Setup all environments
./tmp/production-setup-linux.sh production
./tmp/production-setup-linux.sh staging
./tmp/production-setup-linux.sh development
```

### 2. Configure DNS
Point these domains to HART-SERVER:
- `api.hartonomous.com` ? HART-SERVER IP
- `app.hartonomous.com` ? HART-SERVER IP
- `api-staging.hartonomous.com` ? HART-SERVER IP
- `app-staging.hartonomous.com` ? HART-SERVER IP
- `api-dev.hartonomous.com` ? HART-SERVER IP
- `app-dev.hartonomous.com` ? HART-SERVER IP

### 3. Obtain SSL Certificates
```bash
ssh root@hart-server
certbot --nginx -d api.hartonomous.com -d app.hartonomous.com
certbot --nginx -d api-staging.hartonomous.com -d app-staging.hartonomous.com
certbot --nginx -d api-dev.hartonomous.com -d app-dev.hartonomous.com
```

### 4. Deploy Application
```bash
# From your development machine
cd D:\Repositories\Hartonomous

# Deploy to development first
./deploy/deploy-app.sh development all

# Then staging
./deploy/deploy-app.sh staging all

# Finally production
./deploy/deploy-app.sh production all
```

### 5. Verify Everything Works
```bash
# Check all health endpoints
curl https://api.hartonomous.com/health
curl https://api-staging.hartonomous.com/health
curl https://api-dev.hartonomous.com/health
```

---

## ? Deployment Checklist

### Pre-Deployment
- [ ] DNS records configured
- [ ] Server accessible via SSH
- [ ] PostgreSQL installed and configured
- [ ] Redis installed and configured
- [ ] Azure Key Vault configured with secrets

### Initial Setup (per environment)
- [ ] Run `production-setup-linux.sh {environment}`
- [ ] Obtain SSL certificates
- [ ] Create database
- [ ] Configure environment variables

### Application Deployment
- [ ] Build application locally
- [ ] Deploy to development
- [ ] Test in development
- [ ] Deploy to staging
- [ ] QA testing in staging
- [ ] Deploy to production
- [ ] Verify production health

### Post-Deployment
- [ ] All services running
- [ ] Health checks passing
- [ ] SSL certificates valid
- [ ] Database migrations applied
- [ ] Monitoring configured
- [ ] Logs rotating
- [ ] Backups scheduled

---

## ?? Summary

Your Hartonomous solution now has:

? **Enterprise-grade security** (Zero Trust, rate limiting, JWT auth)  
? **Multi-environment deployment** (dev/staging/production)  
? **Proper directory structure** (`/srv/www/{environment}`)  
? **Automated deployment scripts** (one-command deploy)  
? **systemd services** with security hardening  
? **Nginx reverse proxy** with SSL  
? **Health monitoring** and observability  
? **Complete documentation**

**Ready for production deployment to HART-SERVER! ??**

---

**Last Updated**: 2025-12-04  
**Maintainer**: Anthony Hart  
**Repository**: https://dev.azure.com/aharttn/Hartonomous/_git/Hartonomous
