# DEPLOYMENT

Docker, Kubernetes, cloud, edge.

---

## Docker (Single Instance)

```bash
docker run -d \
  --name hartonomous \
  -p 5432:5432 \
  -e POSTGRES_PASSWORD=secure_password \
  -v postgres_data:/var/lib/postgresql/data \
  hartonomous/postgres:latest
```

---

## Docker Compose (with persistence)

```yaml
version: '3.8'

services:
  postgres:
    image: hartonomous/postgres:latest
    environment:
      POSTGRES_DB: hartonomous
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    deploy:
      resources:
        limits:
          cpus: '4'
          memory: 8G

  pgbouncer:
    image: pgbouncer/pgbouncer:latest
    environment:
      DATABASES_HOST: postgres
      DATABASES_PORT: 5432
      DATABASES_DBNAME: hartonomous
      POOL_MODE: transaction
      MAX_CLIENT_CONN: 1000
      DEFAULT_POOL_SIZE: 25
    ports:
      - "6432:6432"

volumes:
  postgres_data:
```

---

## Kubernetes

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: hartonomous-postgres
spec:
  serviceName: hartonomous
  replicas: 3
  selector:
    matchLabels:
      app: hartonomous
  template:
    metadata:
      labels:
        app: hartonomous
    spec:
      containers:
      - name: postgres
        image: hartonomous/postgres:latest
        ports:
        - containerPort: 5432
        env:
        - name: POSTGRES_PASSWORD
          valueFrom:
            secretKeyRef:
              name: postgres-secret
              key: password
        volumeMounts:
        - name: data
          mountPath: /var/lib/postgresql/data
  volumeClaimTemplates:
  - metadata:
      name: data
    spec:
      accessModes: [ "ReadWriteOnce" ]
      resources:
        requests:
          storage: 100Gi
```

---

## AWS (RDS PostgreSQL)

```bash
# Create RDS instance
aws rds create-db-instance \
  --db-instance-identifier hartonomous \
  --db-instance-class db.r6g.2xlarge \
  --engine postgres \
  --engine-version 15.4 \
  --master-username postgres \
  --master-user-password ${DB_PASSWORD} \
  --allocated-storage 500 \
  --storage-type gp3 \
  --storage-encrypted

# Enable PostGIS
psql -h hartonomous.xxx.rds.amazonaws.com -U postgres -d postgres \
  -c "CREATE EXTENSION postgis;"
```

---

## GCP (Cloud SQL)

```bash
gcloud sql instances create hartonomous \
  --database-version=POSTGRES_15 \
  --tier=db-custom-8-32768 \
  --region=us-central1 \
  --storage-size=500GB \
  --storage-type=SSD

# Enable extensions
gcloud sql connect hartonomous --user=postgres
CREATE EXTENSION postgis;
```

---

## Azure (Azure Database for PostgreSQL)

```bash
az postgres flexible-server create \
  --name hartonomous \
  --resource-group myResourceGroup \
  --location eastus \
  --sku-name Standard_D8ds_v4 \
  --tier GeneralPurpose \
  --storage-size 512 \
  --version 15

# Enable extensions via portal or CLI
```

---

## Edge (Raspberry Pi)

```bash
# ARM-compatible image
docker pull hartonomous/postgres:arm64

docker run -d \
  --name hartonomous \
  -p 5432:5432 \
  -e POSTGRES_PASSWORD=password \
  -v /mnt/usb/postgres:/var/lib/postgresql/data \
  hartonomous/postgres:arm64
```

---

## Replication (Read Scaling)

```bash
# Primary
docker run -d \
  --name hartonomous-primary \
  -e POSTGRES_PASSWORD=password \
  -e POSTGRES_REPLICATION_MODE=master \
  -e POSTGRES_REPLICATION_USER=replicator \
  -e POSTGRES_REPLICATION_PASSWORD=replpass \
  hartonomous/postgres:latest

# Replica
docker run -d \
  --name hartonomous-replica \
  -e POSTGRES_PASSWORD=password \
  -e POSTGRES_REPLICATION_MODE=slave \
  -e POSTGRES_MASTER_HOST=hartonomous-primary \
  -e POSTGRES_REPLICATION_USER=replicator \
  -e POSTGRES_REPLICATION_PASSWORD=replpass \
  hartonomous/postgres:latest
```

---

## Backup

```bash
# Dump
pg_dump -h localhost -U postgres hartonomous > backup.sql

# Restore
psql -h localhost -U postgres hartonomous < backup.sql

# Continuous archiving (WAL)
postgresql.conf:
  wal_level = replica
  archive_mode = on
  archive_command = 'cp %p /backup/wal/%f'
```

---

## Monitoring

```yaml
# Prometheus + Grafana
version: '3.8'
services:
  postgres_exporter:
    image: prometheuscommunity/postgres-exporter
    environment:
      DATA_SOURCE_NAME: "postgresql://postgres:password@hartonomous:5432/hartonomous?sslmode=disable"
    ports:
      - "9187:9187"

  prometheus:
    image: prom/prometheus
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    ports:
      - "9090:9090"

  grafana:
    image: grafana/grafana
    ports:
      - "3000:3000"
```

Key metrics:
- Query latency (avg, p95, p99)
- Atom count
- Spatial index size
- Reference count distribution
- OODA loop cycle time

---

## Security

```sql
-- Row-level security
ALTER TABLE atom ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON atom
  USING (metadata->>'tenantId' = current_setting('app.tenant_id'));

-- SSL/TLS
postgresql.conf:
  ssl = on
  ssl_cert_file = '/path/to/server.crt'
  ssl_key_file = '/path/to/server.key'
```

---

## Performance Tuning

```ini
# postgresql.conf
shared_buffers = 25% of RAM
effective_cache_size = 75% of RAM
work_mem = 64MB
maintenance_work_mem = 1GB
max_parallel_workers_per_gather = 4
random_page_cost = 1.1  # SSD
```

---

**See Also**: [02-ARCHITECTURE.md](02-ARCHITECTURE.md) for configuration details
