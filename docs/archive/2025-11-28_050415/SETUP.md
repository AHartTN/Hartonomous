# Hartonomous Setup Guide

## Quick Start

### 1. Install Dependencies

```bash
# Install Python dependencies
cd api
pip3 install -r requirements.txt
```

### 2. Configure Credentials

Your credentials are stored securely in `.env.hart-server` (machine-specific, already created and gitignored):

```bash
# Setup HuggingFace and Ollama credentials
./scripts/setup-credentials.sh

# Reload shell to pick up environment variables
source ~/.bashrc
```

### 3. Install Ollama

```bash
# Install Ollama to /var/workload/ollama
./scripts/install-ollama.sh

# Reload shell
source ~/.bashrc

# Start Ollama service
ollama serve &

# Pull a model (optional - for testing)
ollama pull llama3.2
```

### 4. Start Services with Docker

```bash
# Start PostgreSQL, Neo4j, and API services
docker compose up -d

# Check service health
docker compose ps
docker compose logs -f api
```

## Credentials Configuration

Your credentials are securely stored in machine-specific environment files:

- **hart-server**: `.env.hart-server`
- **hart-desktop**: `.env.hart-desktop`
- **fallback**: `.env.local`

Each file contains:
- **HuggingFace Token**: For accessing models and datasets
- **Ollama API Key**: For Ollama cloud features
- **Database Passwords**: PostgreSQL and Neo4j credentials

### Security Notes

✅ All `.env.*` files are gitignored (except `.env.example`)
✅ File permissions set to 600 (owner read/write only)
✅ Credentials automatically loaded in shell sessions based on machine
✅ Each machine can have different credentials without conflicts

## Directory Structure

```
/var/workload/
├── Repositories/Github/AHartTN/Hartonomous/  # Project repository
│   ├── .env.local                              # Your credentials (gitignored)
│   ├── api/                                    # FastAPI application
│   ├── scripts/                                # Setup scripts
│   └── docker-compose.yml                      # Service orchestration
│
└── ollama/                                     # Ollama installation
    ├── bin/ollama                              # Ollama binary
    └── models/                                 # Downloaded models (4-30GB each)

/var/lib/docker/                                # Docker storage (444GB available)
└── volumes/
    ├── hartonomous_postgres_data/              # PostgreSQL data
    └── hartonomous_neo4j_data/                 # Neo4j graph data
```

## Testing

```bash
# Run all tests
python3 -m pytest -v

# Run only unit tests
python3 -m pytest tests/test_*.py -v

# Run with coverage
python3 -m pytest --cov=api --cov-report=html
```

Current status: **29/29 tests passing ✅**

## Common Commands

```bash
# Start services
docker compose up -d

# Stop services
docker compose down

# View logs
docker compose logs -f api
docker compose logs -f postgres

# Restart a service
docker compose restart api

# Access PostgreSQL
psql -h localhost -U hartonomous -d hartonomous

# Access Neo4j browser
open http://localhost:7474

# Check Ollama status
ollama list
ollama ps
```

## Troubleshooting

### Ollama Not Found

```bash
# Ensure Ollama is in PATH
source ~/.bashrc
which ollama
```

### Database Connection Issues

```bash
# Check if PostgreSQL is running
docker compose ps postgres

# Check logs
docker compose logs postgres

# Verify connection
psql -h localhost -U hartonomous -d hartonomous -c "SELECT version();"
```

### Model Storage Space

```bash
# Check available space on workload drive
df -h /var/workload

# List Ollama models and their sizes
ollama list
```

## Next Steps

1. **Pull LLM models** for code atomization
2. **Initialize database schema** with Alembic migrations
3. **Ingest code repositories** for analysis
4. **Start building knowledge graphs**

## Documentation

- [Architecture Overview](docs/02-ARCHITECTURE.md)
- [API Documentation](http://localhost:8000/docs) (when running)
- [Implementation Guide](IMPLEMENTATION_COMPLETE.md)
