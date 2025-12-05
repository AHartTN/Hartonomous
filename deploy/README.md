# Hartonomous Deployment

Complete deployment configuration for Hartonomous on Azure Arc-enabled infrastructure.

## Architecture

**Linux Server (hart-server):**
- `/srv/www/hartonomous/api` - ASP.NET Core API
- `/srv/www/hartonomous/worker` - Background Worker
- PostgreSQL (256GB), Redis (128GB), MSSQL (128GB), Neo4j (128GB)

**Windows Desktop (hart-desktop):**
- `D:\inetpub\hartonomous\web` - Blazor Web App (IIS)

**Azure Integration:**
- Azure Arc-enabled servers (both Linux & Windows)
- Azure Key Vault for secrets (Managed Identity auth)
- Entra ID for internal authentication
- External ID for customer authentication
- Azure DevOps Pipelines with "Local Agent Pool"

## Deployment Order

### 1. Database Setup
```bash
cd database

# Initialize PostgreSQL
./setup-postgresql.sh

# Configure Redis
./configure-redis.sh
```

### 2. Azure Key Vault
```bash
cd azure

# Store secrets in Key Vault
./setup-keyvault-secrets.sh

# Grant Arc servers access
# Edit script first to set resource group & server names
./grant-arc-keyvault-access.sh
```

### 3. Application Configuration
```bash
cd config

# Deploy appsettings (gets secrets from Key Vault)
sudo cp appsettings.Production.json /srv/www/hartonomous/api/
sudo cp appsettings.Production.json /srv/www/hartonomous/worker/
```

### 4. Systemd Services
```bash
cd systemd

# Install services
./install-services.sh

# Start services (after first deployment)
sudo systemctl start hartonomous-api
sudo systemctl start hartonomous-worker
```

### 5. Nginx Reverse Proxy
```bash
cd nginx

# Install and configure
./install-nginx.sh

# Obtain SSL certificate
sudo certbot --nginx -d api.hartonomous.local
```

## Directory Structure

```
deployment/
├── systemd/                    # Service definitions
│   ├── hartonomous-api.service
│   ├── hartonomous-worker.service
│   └── install-services.sh
├── nginx/                      # Reverse proxy
│   ├── hartonomous-api.conf
│   └── install-nginx.sh
├── config/                     # Application settings
│   ├── appsettings.Production.json
│   └── setup-config.sh
├── database/                   # Database initialization
│   ├── init-postgresql.sql
│   ├── setup-postgresql.sh
│   ├── configure-redis.sh
│   └── README.md
├── azure/                      # Azure integration
│   ├── setup-keyvault-secrets.sh
│   ├── grant-arc-keyvault-access.sh
│   └── README.md
└── README.md                   # This file
```

## Azure DevOps Pipeline

The pipeline automatically:
1. Builds all projects on "Local Agent Pool"
2. MAUI app builds on Windows agent (hart-desktop)
3. Creates NuGet packages (Core, Data, Infrastructure)
4. Deploys to "Primary Local" deployment group
   - API & Worker → Linux (hart-server)
   - Web → Windows IIS (hart-desktop)

**Pipeline file:** `.azure-pipelines/build-and-deploy.yml`

## Infrastructure Resources

### Linux (hart-server)
```
Hosting:     /srv/www (16GB XFS on vg-hosting)
Databases:
  - PostgreSQL: /var/lib/postgresql (256GB)
  - Redis:      /var/lib/redis (128GB)
  - MSSQL:      /var/opt/mssql (128GB)
  - Neo4j:      /var/lib/neo4j (128GB)
```

### Windows (hart-desktop)
```
Hosting:  D:\inetpub (2x 2TB Samsung 990 EVO NVMe)
Services: IIS, .NET 10 Runtime, MAUI Workload
```

## Security

**Zero Trust Model:**
- No passwords in config files
- Azure Key Vault for all secrets
- Managed Identity authentication (no service principals)
- Entra ID authentication for users
- External ID for customers
- SSL/TLS for all external endpoints

**Access Control:**
```bash
# Service accounts
User: www-data
Group: www-data

# Permissions
/srv/www/hartonomous: 755 (www-data:www-data)
/var/lib/postgresql:  700 (postgres:postgres)
/var/lib/redis:       755 (redis:redis)
```

## Monitoring

### Service Status
```bash
# Check services
sudo systemctl status hartonomous-api
sudo systemctl status hartonomous-worker
sudo systemctl status redis-server
sudo systemctl status postgresql

# View logs
sudo journalctl -u hartonomous-api -f
sudo journalctl -u hartonomous-worker -f
```

### Database Health
```bash
# PostgreSQL
sudo -u postgres psql -d hartonomous -c "SELECT count(*) FROM pg_stat_activity;"

# Redis
redis-cli ping
redis-cli info memory
```

### Nginx
```bash
# Test config
sudo nginx -t

# Reload
sudo systemctl reload nginx

# Logs
sudo tail -f /var/log/nginx/hartonomous-api-*.log
```

## Troubleshooting

### API won't start
```bash
# Check executable permissions
sudo chmod +x /srv/www/hartonomous/api/Hartonomous.Api

# Check dependencies
ldd /srv/www/hartonomous/api/Hartonomous.Api

# View detailed logs
sudo journalctl -u hartonomous-api -n 100 --no-pager
```

### Database connection issues
```bash
# Test PostgreSQL
psql -h localhost -U hartonomous_user -d hartonomous

# Test Redis
redis-cli ping

# Check Key Vault access
az login --identity
az keyvault secret show --vault-name hartonomous-kv --name PostgreSQL--Password
```

### Managed Identity issues
```bash
# Check Arc agent
sudo azcmagent show

# Restart agent
sudo systemctl restart himdsd

# Test token acquisition
curl -H "Metadata:true" \
  "http://localhost:40342/metadata/identity/oauth2/token?api-version=2020-06-01&resource=https://vault.azure.net"
```

## Backup Strategy

### PostgreSQL
```bash
# Daily backup (add to crontab)
0 2 * * * sudo -u postgres pg_dump hartonomous | gzip > /srv/archive/hartonomous_$(date +\%Y\%m\%d).sql.gz
```

### Redis
```bash
# Automatic via RDB + AOF
# Backup files: /var/lib/redis/dump.rdb, /var/lib/redis/appendonly.aof
```

### Application
```bash
# Backup config and binaries
tar -czf hartonomous_backup_$(date +%Y%m%d).tar.gz /srv/www/hartonomous
```

## Updates and Maintenance

### Update via Pipeline
Commits to `main` branch automatically trigger:
1. Build
2. Test
3. Deploy to Primary Local environment

### Manual Restart
```bash
# Restart services after config change
sudo systemctl restart hartonomous-api
sudo systemctl restart hartonomous-worker

# Restart Nginx
sudo systemctl reload nginx
```

### Database Migrations
```bash
# Run from API directory
cd /srv/www/hartonomous/api
./Hartonomous.Api --migrate-database
```

## Support

**Logs Location:**
- Services: `journalctl -u hartonomous-*`
- Nginx: `/var/log/nginx/hartonomous-*.log`
- PostgreSQL: `/var/log/postgresql/`
- Redis: `journalctl -u redis-server`

**Configuration:**
- App Settings: `/srv/www/hartonomous/*/appsettings.Production.json`
- Secrets: Azure Key Vault `hartonomous-kv`
- Services: `/etc/systemd/system/hartonomous-*.service`
- Nginx: `/etc/nginx/sites-available/hartonomous-*`
