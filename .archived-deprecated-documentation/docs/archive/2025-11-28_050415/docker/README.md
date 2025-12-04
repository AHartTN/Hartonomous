# Hartonomous Docker Deployment

**Production-ready Docker Compose setup**

---

## ?? Quick Start

### 1. Prerequisites

```bash
- Docker 20.10+
- Docker Compose 2.0+
```

### 2. Configure Environment

```bash
cp .env.example .env
# Edit .env if needed (default settings work out of box)
```

### 3. Start Services

```bash
docker-compose up -d
```

### 4. Verify

```bash
# Check services
docker-compose ps

# Check health
curl http://localhost:8000/v1/health
curl http://localhost:8000/v1/ready

# View logs
docker-compose logs -f api
```

---

## ?? Services

### PostgreSQL (hartonomous-postgres)

**Image:** postgres:15-alpine  
**Port:** 5432  
**Volumes:** 
- `postgres_data:/var/lib/postgresql/data` (persistent storage)
- `./schema:/docker-entrypoint-initdb.d` (schema initialization)

**Health Check:** `pg_isready` every 10s

---

### FastAPI (hartonomous-api)

**Build:** `docker/Dockerfile`  
**Port:** 8000  
**Depends On:** postgres (waits for healthy)

**Health Check:** `GET /v1/health` every 10s

---

## ?? Configuration

### Environment Variables

See `.env.example` for all options.

**Key settings:**

```bash
# Database
PGUSER=postgres
PGPASSWORD=postgres
PGDATABASE=hartonomous
PGPORT=5432

# API
API_PORT=8000
LOG_LEVEL=INFO

# AGE Worker
AGE_WORKER_ENABLED=true
```

---

## ??? Management Commands

### Start Services

```bash
docker-compose up -d
```

### Stop Services

```bash
docker-compose down
```

### Restart Services

```bash
docker-compose restart
```

### View Logs

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f api
docker-compose logs -f postgres
```

### Execute Commands

```bash
# PostgreSQL
docker-compose exec postgres psql -U postgres -d hartonomous

# API shell
docker-compose exec api /bin/bash
```

### Rebuild

```bash
# Rebuild API image
docker-compose build api

# Rebuild and restart
docker-compose up -d --build
```

---

## ?? Monitoring

### Health Checks

```bash
# API health
curl http://localhost:8000/v1/health

# API readiness (tests DB)
curl http://localhost:8000/v1/ready

# Database statistics
curl http://localhost:8000/v1/stats
```

### Logs

```bash
# Stream logs
docker-compose logs -f

# Last 100 lines
docker-compose logs --tail=100

# Service-specific
docker-compose logs -f api
```

---

## ?? Security

### Production Hardening

**1. Change default passwords**

```bash
# .env
PGPASSWORD=your-secure-password
```

**2. Enable SSL/TLS**

```yaml
# docker-compose.yml
postgres:
  command: -c ssl=on -c ssl_cert_file=/etc/ssl/certs/server.crt
  volumes:
    - ./certs:/etc/ssl/certs
```

**3. Restrict ports**

```yaml
# docker-compose.yml
ports:
  - "127.0.0.1:5432:5432"  # Only localhost
```

**4. Enable authentication**

```bash
# .env
AUTH_ENABLED=true
AUTH_SECRET_KEY=your-jwt-secret
```

---

## ?? Testing

### Integration Tests

```bash
# Start services
docker-compose up -d

# Wait for healthy
sleep 10

# Run tests
curl -X POST http://localhost:8000/v1/ingest/text \
  -H "Content-Type: application/json" \
  -d '{"text": "Hello World"}'

# Check result
curl http://localhost:8000/v1/stats
```

---

## ?? Troubleshooting

### Services won't start

```bash
# Check status
docker-compose ps

# View logs
docker-compose logs

# Restart
docker-compose down
docker-compose up -d
```

### Database connection failed

```bash
# Check postgres health
docker-compose exec postgres pg_isready

# Check network
docker network inspect hartonomous_hartonomous-network

# Check environment
docker-compose exec api env | grep DATABASE_URL
```

### Port already in use

```bash
# Change ports in .env
API_PORT=8001
PGPORT=5433

# Restart
docker-compose down
docker-compose up -d
```

---

## ?? Documentation

- [API Documentation](../api/README.md)
- [Schema Documentation](../schema/README.md)
- [Deployment Guide](../docs/deployment/README.md)

---

**Quick Links:**
- [Swagger UI](http://localhost:8000/docs)
- [ReDoc](http://localhost:8000/redoc)
- [Health Check](http://localhost:8000/v1/health)

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
