# Hartonomous Local Development Setup

**Simple prototype setup for local development**

## Prerequisites

- ✅ SQL Server 2025 (already installed and verified)
- ✅ Neo4j (local instance needed)
- ✅ .NET 8 SDK
- ✅ Visual Studio or VS Code

## Quick Local Setup

### 1. SQL Server 2025 (Already Working!)
```sql
-- Your SQL Server 2025 is already configured with:
-- - VECTOR data type support ✅
-- - T-SQL REST endpoint support ✅
-- - CLR integration ✅
-- - FILESTREAM support ✅

-- Test database creation:
CREATE DATABASE HartonomousDB_Local;
```

### 2. Neo4j Local Setup
```bash
# Option 1: Docker (Recommended for prototype)
docker run -d \
  --name neo4j-hartonomous \
  -p 7474:7474 -p 7687:7687 \
  -e NEO4J_AUTH=neo4j/development \
  neo4j:latest

# Option 2: Local install
# Download from https://neo4j.com/download/
# Set password to "development"
```

### 3. Development Configuration

**All development configs now use localhost:**
- **SQL Server**: `Server=localhost;Database=HartonomousDB;Trusted_Connection=true`
- **Neo4j**: `bolt://localhost:7687` with `neo4j/development`
- **Key Vault**: Disabled for development (`EnableInDevelopment: false`)
- **Azure**: Only needed for production deployment

### 4. Start Development Services

```bash
# Terminal 1: API Service
cd src/Api/Hartonomous.Api
dotnet run

# Terminal 2: Model Query Service
cd src/Services/Hartonomous.ModelQuery
dotnet run

# Terminal 3: MCP Service
cd src/Services/Hartonomous.MCP
dotnet run
```

### 5. Quick Test

```bash
# Test SQL Server 2025 vector capabilities
sqlcmd -S localhost -Q "SELECT VECTOR_DISTANCE('cosine', '[1,0,0,0]', '[0,1,0,0]') AS TestDistance"

# Test Neo4j connection
curl -u neo4j:development http://localhost:7474/db/neo4j/tx/commit \
  -H "Content-Type: application/json" \
  -d '{"statements":[{"statement":"RETURN 1 as test"}]}'
```

## Development URLs

- **API**: http://localhost:5000
- **Model Query Service**: http://localhost:5002
- **MCP Service**: http://localhost:5001
- **Neo4j Browser**: http://localhost:7474

## What's Different from Production

### Development (Local)
- No Azure Key Vault dependency
- No external Neo4j server (192.168.1.2)
- Simple passwords in config files
- No Milvus (replaced by SQL Server 2025 vectors)
- Local file storage for model weights

### Production (Deployed)
- Azure Key Vault for secrets
- Azure App Configuration for settings
- External Neo4j cluster
- Service principal authentication
- Azure Blob Storage for models

## Next Steps for Prototype

1. **Remove Milvus code references** (we deleted the config already)
2. **Test SQL Server vector operations** with real embeddings
3. **Build T-SQL REST integration** with llama.cpp service
4. **Create simple agent distillation** pipeline
5. **Test constitutional AI** safety framework

## Troubleshooting

### SQL Server Issues
```bash
# Check SQL Server services
sqlcmd -S localhost -Q "SELECT @@VERSION"

# Verify vector support
sqlcmd -S localhost -Q "CREATE TABLE test (v VECTOR(4)); DROP TABLE test;"
```

### Neo4j Issues
```bash
# Check Neo4j status
docker logs neo4j-hartonomous

# Test connection
docker exec neo4j-hartonomous cypher-shell -u neo4j -p development "RETURN 1"
```

### Build Issues
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

---

*This setup focuses on local development simplicity for prototyping the revolutionary NinaDB + AI Agent Factory architecture.*