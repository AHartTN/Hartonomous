# Hartonomous HART-SERVER Deployment Quick Reference

## ?? Deployment Structure

```
/srv/www/
??? production/
?   ??? api/              # Port 5000
?   ??? worker/
?   ??? web/
??? staging/
?   ??? api/              # Port 5001
?   ??? worker/
?   ??? web/
??? development/
    ??? api/              # Port 5002
    ??? worker/
    ??? web/

/var/log/hartonomous/
??? production/
??? staging/
??? development/

/var/lib/hartonomous/
??? production/
??? staging/
??? development/
```

## ?? Quick Deployment Commands

### Initial Server Setup

```bash
# Production environment
sudo ./deploy/production-setup-linux.sh production

# Staging environment
sudo ./deploy/production-setup-linux.sh staging

# Development environment
sudo ./deploy/production-setup-linux.sh development
```

### Deploy Application

```bash
# Deploy all components to production
./deploy/deploy-app.sh production all

# Deploy only API to staging
./deploy/deploy-app.sh staging api

# Deploy worker to development
./deploy/deploy-app.sh development worker

# Deploy web app to production
./deploy/deploy-app.sh production web
```

## ?? SSL Certificate Setup

```bash
# Production
sudo certbot --nginx -d api.hartonomous.com -d app.hartonomous.com

# Staging
sudo certbot --nginx -d api-staging.hartonomous.com -d app-staging.hartonomous.com

# Development
sudo certbot --nginx -d api-dev.hartonomous.com -d app-dev.hartonomous.com
```

## ?? Service Management

### Production
```bash
sudo systemctl start hartonomous-api-production
sudo systemctl start hartonomous-worker-production
sudo systemctl status hartonomous-api-production
sudo journalctl -u hartonomous-api-production -f
```

### Staging
```bash
sudo systemctl start hartonomous-api-staging
sudo systemctl start hartonomous-worker-staging
sudo systemctl status hartonomous-api-staging
sudo journalctl -u hartonomous-api-staging -f
```

### Development
```bash
sudo systemctl start hartonomous-api-development
sudo systemctl start hartonomous-worker-development
sudo systemctl status hartonomous-api-development
sudo journalctl -u hartonomous-api-development -f
```

## ?? Health Check Endpoints

| Environment | API Health | Web App |
|------------|-----------|---------|
| Production | https://api.hartonomous.com/health | https://app.hartonomous.com |
| Staging | https://api-staging.hartonomous.com/health | https://app-staging.hartonomous.com |
| Development | https://api-dev.hartonomous.com/health | https://app-dev.hartonomous.com |

## ??? Database Setup

```bash
# Create databases
sudo -u postgres psql
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

## ?? Environment Configuration

| Setting | Production | Staging | Development |
|---------|-----------|---------|-------------|
| **Port** | 5000 | 5001 | 5002 |
| **Rate Limit** | 1000/min | 5000/min | 10000/min |
| **Max Connections** | 1000 | 500 | 100 |
| **Log Level** | Information | Information | Debug |
| **Debug Mode** | ? | ? | ? |

## ?? Typical Deployment Workflow

```bash
# 1. Make code changes locally
git add .
git commit -m "Feature: Added new endpoint"
git push origin main

# 2. Deploy to development first
./deploy/deploy-app.sh development all

# 3. Test in development
curl https://api-dev.hartonomous.com/health

# 4. Deploy to staging for UAT
./deploy/deploy-app.sh staging all

# 5. After approval, deploy to production
./deploy/deploy-app.sh production all

# 6. Verify production
curl https://api.hartonomous.com/health
```

## ?? Quick Troubleshooting

```bash
# Check if service is running
sudo systemctl status hartonomous-api-{environment}

# View recent logs
sudo journalctl -u hartonomous-api-{environment} -n 100

# Restart service
sudo systemctl restart hartonomous-api-{environment}

# Check Nginx configuration
sudo nginx -t
sudo systemctl reload nginx

# Check port bindings
sudo netstat -tlnp | grep -E '5000|5001|5002'

# View Nginx logs
sudo tail -f /var/log/nginx/hartonomous-{environment}-api-error.log
```

## ?? Environment Variables Reference

Set these in `/etc/environment` or systemd service files:

```bash
# Database
DB_PASSWORD=your_secure_password

# Azure
TENANT_ID=your_tenant_id
CLIENT_ID=your_client_id

# Optional
REDIS_PASSWORD=your_redis_password
```

## ?? Port Reference

- **5000** - Production API
- **5001** - Staging API
- **5002** - Development API
- **6379** - Redis
- **5432** - PostgreSQL
- **80/443** - Nginx (HTTP/HTTPS)

## ??? Security Checklist

- [ ] SSL certificates configured for all environments
- [ ] Firewall rules active (UFW)
- [ ] Rate limiting enabled
- [ ] Security headers configured
- [ ] Secrets stored in Azure Key Vault
- [ ] systemd sandboxing active
- [ ] Log rotation configured
- [ ] Automatic updates enabled

---

**Last Updated**: 2025-12-04  
**Maintainer**: Anthony Hart
