# Hartonomous Database Setup

This directory contains database initialization scripts for Hartonomous.

## Quick Start

Run scripts in this order:

```bash
# 1. Initialize PostgreSQL
cd database
chmod +x setup-postgresql.sh
./setup-postgresql.sh

# 2. Configure Redis (already installed)
chmod +x configure-redis.sh
./configure-redis.sh
```

## Database Layout

### PostgreSQL (256GB @ /var/lib/postgresql)
- **Database:** hartonomous
- **User:** hartonomous_user
- **Schemas:**
  - `cas` - Content-Addressable Storage
  - `bpe` - Byte-Pair Encoding
  - `meta` - Metadata and file management

### Redis (128GB @ /var/lib/redis)
- **Port:** 6379
- **Max Memory:** 16GB (configurable)
- **Persistence:** RDB + AOF enabled

### SQL Server (128GB @ /var/opt/mssql)
- Available for future use

### Neo4j (128GB @ /var/lib/neo4j)
- Available for future use

## Database Schemas

### Content-Addressable Storage (cas schema)
- `cas.atoms` - Atomic content chunks with content hashing
- Hilbert curve indexing for spatial locality
- Automatic deduplication via content hash

### Byte-Pair Encoding (bpe schema)
- `bpe.vocabulary` - Token vocabulary
- Frequency tracking for compression

### Metadata (meta schema)
- `meta.files` - File metadata and JSONB properties
- `meta.file_atoms` - File-to-atom mapping (many-to-many)

## Connection Strings

Update these in `/srv/www/hartonomous/.env`:

```bash
# PostgreSQL
POSTGRES_CONNECTION_STRING="Host=localhost;Port=5432;Database=hartonomous;Username=hartonomous_user;Password=YOUR_PASSWORD"

# Redis
REDIS_CONNECTION_STRING="localhost:6379,abortConnect=false"
```

## Backup and Maintenance

### PostgreSQL Backup
```bash
# Full database backup
sudo -u postgres pg_dump hartonomous > hartonomous_backup_$(date +%Y%m%d).sql

# Compressed backup
sudo -u postgres pg_dump hartonomous | gzip > hartonomous_backup_$(date +%Y%m%d).sql.gz

# Restore
sudo -u postgres psql hartonomous < hartonomous_backup.sql
```

### Redis Backup
```bash
# Create backup (triggers RDB save)
redis-cli BGSAVE

# Backup files location
/var/lib/redis/dump.rdb
/var/lib/redis/appendonly.aof
```

## Monitoring

### PostgreSQL
```bash
# Check database size
sudo -u postgres psql -d hartonomous -c "\l+ hartonomous"

# Check table sizes
sudo -u postgres psql -d hartonomous -c "\dt+ cas.*"

# Active connections
sudo -u postgres psql -d hartonomous -c "SELECT count(*) FROM pg_stat_activity;"
```

### Redis
```bash
# Memory usage
redis-cli info memory

# Key count
redis-cli dbsize

# Monitor commands in real-time
redis-cli monitor
```

## Storage Volumes

All databases are on dedicated LVM volumes on NVMe0 (database-vg):

```
/var/lib/postgresql - 256GB (PostgreSQL)
/var/lib/redis      - 128GB (Redis)
/var/opt/mssql      - 128GB (SQL Server - available)
/var/lib/neo4j      - 128GB (Neo4j - available)
Free space          - ~291GB (for expansion)
```

## Troubleshooting

### PostgreSQL won't start
```bash
# Check status
sudo systemctl status postgresql

# View logs
sudo journalctl -u postgresql -n 50

# Check disk space
df -h /var/lib/postgresql
```

### Redis connection issues
```bash
# Check if running
sudo systemctl status redis-server

# Test connection
redis-cli ping

# Check logs
sudo journalctl -u redis-server -n 50
```

### Permission issues
```bash
# Fix PostgreSQL permissions
sudo chown -R postgres:postgres /var/lib/postgresql

# Fix Redis permissions
sudo chown -R redis:redis /var/lib/redis
```
