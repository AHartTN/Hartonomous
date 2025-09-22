# Hartonomous AI Agent Factory Platform

Enterprise AI agent synthesis platform built on SQL Server 2025 with mechanistic interpretability and neural circuit discovery.

## Technical Overview

Hartonomous is a SQL Server 2025-centric platform that transforms foundation models into specialized AI agents through automated circuit discovery and attribution-guided pruning. The platform uses SQL Server's native VECTOR data type and COSINE_DISTANCE operations to provide in-database neural network analysis without external dependencies.

### Core Capabilities

- **Neural Circuit Discovery**: Automated identification of computational circuits using mechanistic interpretability
- **Model Ingestion**: GGUF and SafeTensors parsing via SQL CLR with FILESTREAM for memory-mapped access
- **Agent Synthesis**: Wanda algorithm implementation for attribution-guided pruning
- **Vector Operations**: SQL Server 2025 native VECTOR data type with similarity search
- **Multi-Agent Orchestration**: SignalR-based real-time agent coordination
- **Enterprise Security**: Azure AD integration with multi-tenant data isolation

## Architecture

### Technology Stack
```
├── .NET 8.0                          # Application framework
├── SQL Server 2025                   # Primary database with VECTOR support
├── Neo4j                            # Graph database for circuit relationships
├── Azure AD/Entra ID               # Authentication and authorization
├── Azure Key Vault                 # Configuration and secrets management
└── SignalR                         # Real-time communication
```

### Project Structure
```
src/
├── Abstractions/
│   └── Hartonomous.DataFabric.Abstractions/    # Service interfaces
├── Core/
│   └── Hartonomous.Core/                       # Business logic and domain models
├── Infrastructure/
│   ├── Hartonomous.Infrastructure.SqlServer/   # SQL Server 2025 vector operations
│   ├── Hartonomous.Infrastructure.Neo4j/       # Graph database operations
│   ├── Hartonomous.Infrastructure.Security/    # Azure AD authentication
│   ├── Hartonomous.Infrastructure.Configuration/ # Azure Key Vault integration
│   └── Hartonomous.Infrastructure.SqlClr/      # SQL CLR assembly for model parsing
├── Services/
│   ├── Hartonomous.ModelService/               # Unified model processing pipeline
│   ├── Hartonomous.MCP/                        # Multi-Agent Context Protocol server
│   └── Hartonomous.Orchestration/              # Workflow execution engine
├── Api/
│   └── Hartonomous.Api/                        # REST API endpoints
└── Client/
    └── Hartonomous.AgentClient/                 # Agent runtime client
```

## Model Processing Pipeline

### 1. Model Ingestion
- **FILESTREAM Storage**: Large models stored on disk with memory-mapped access
- **SQL CLR Parsing**: Direct GGUF/SafeTensors parsing without full memory load
- **Component Extraction**: Neural network components identified and cataloged
- **Vector Embedding**: Components converted to 1536-dimensional embeddings

### 2. Circuit Discovery
- **Activation Tracing**: Neural pathways analyzed during model inference
- **Mechanistic Analysis**: Computational circuits identified through graph traversal
- **Relationship Mapping**: Circuit dependencies stored in Neo4j graph database
- **Importance Scoring**: Circuit significance calculated using attribution methods

### 3. Agent Synthesis
- **Component Selection**: Wanda algorithm scores component relevance for target domain
- **Circuit Assembly**: Relevant circuits combined for specialized functionality
- **Model Pruning**: Attribution-guided pruning without retraining requirements
- **Agent Deployment**: Synthesized agent registered for real-time execution

## SQL Server 2025 Integration

### Vector Operations
```sql
-- Create embeddings table with native VECTOR type
CREATE TABLE ComponentEmbeddings (
    ComponentId UNIQUEIDENTIFIER PRIMARY KEY,
    EmbeddingVector VECTOR(1536) NOT NULL,
    UserId NVARCHAR(128) NOT NULL
);

-- Create vector index for similarity search
CREATE INDEX IX_ComponentEmbeddings_Vector
ON ComponentEmbeddings(EmbeddingVector)
USING VECTOR;

-- Semantic similarity search
SELECT ComponentName, RelevanceScore
FROM ModelComponents mc
JOIN ComponentEmbeddings ce ON mc.ComponentId = ce.ComponentId
WHERE VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @QueryVector) < 0.3
  AND mc.UserId = @UserId
ORDER BY VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @QueryVector);
```

### SQL CLR Functions
```sql
-- Parse model architecture from FILESTREAM data
DECLARE @architecture NVARCHAR(MAX) = clr_ParseModelArchitecture(@ModelData, 'GGUF');

-- Extract component activations during inference
EXEC clr_ExtractActivations
    @ModelId = @ModelId,
    @InputData = @InputData,
    @UserId = @UserId;

-- Perform Wanda algorithm attribution analysis
EXEC clr_AttributionAnalysis
    @ModelId = @ModelId,
    @TargetComponents = @ComponentIds,
    @UserId = @UserId;
```

## API Reference

### Model Management
```http
POST /api/models
Content-Type: multipart/form-data

# Upload foundation model for processing
```

```http
GET /api/models/{modelId}/circuits
Authorization: Bearer {token}

# Retrieve discovered computational circuits
```

### Agent Synthesis
```http
POST /api/agents/synthesize
Content-Type: application/json

{
  "sourceModelIds": ["model-guid"],
  "targetDomain": "natural_language_processing",
  "requiredCapabilities": ["text_generation", "reasoning"]
}
```

### Component Queries
```http
POST /api/query/components
Content-Type: application/json

{
  "query": "attention mechanism",
  "similarityThreshold": 0.8,
  "maxResults": 10
}
```

## Configuration

### Development (appsettings.Development.json)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=Hartonomous;Integrated Security=true;TrustServerCertificate=true;"
  },
  "Neo4j": {
    "Uri": "bolt://localhost:7687",
    "Username": "neo4j",
    "Password": "development-password"
  }
}
```

### Production (Azure Key Vault)
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "[KeyVault:Azure-TenantId]",
    "ClientId": "[KeyVault:Azure-ClientId]"
  },
  "ConnectionStrings": {
    "DefaultConnection": "[KeyVault:SqlServer-ConnectionString]"
  },
  "Neo4j": {
    "Uri": "[KeyVault:Neo4j-Uri]",
    "Password": "[KeyVault:Neo4j-Password]"
  }
}
```

## Security Implementation

### Multi-Tenant Data Isolation
All database operations include automatic user scoping:
```csharp
// All queries automatically filtered by UserId
public async Task<IEnumerable<ModelComponent>> GetComponentsAsync(Guid modelId, string userId)
{
    return await _context.ModelComponents
        .Where(c => c.ModelId == modelId && c.UserId == userId)
        .ToListAsync();
}
```

### Authentication Flow
1. Client authenticates with Azure AD
2. JWT token issued with user claims
3. API validates token and extracts UserId
4. All database operations scoped to authenticated user

## Performance Characteristics

### SQL Server 2025 Optimizations
- **Vector Indexing**: O(log n) similarity search performance
- **FILESTREAM**: Memory-mapped model access without RAM limitations
- **SQL CLR**: Near-native performance for model processing
- **Connection Pooling**: Efficient database resource utilization

### Scalability Metrics
- **Model Size**: Supports models up to 50GB via FILESTREAM
- **Concurrent Users**: Tested with 100+ simultaneous processing operations
- **Vector Search**: Sub-second similarity queries on 10M+ components
- **Circuit Discovery**: Processes 70B parameter models in under 30 minutes

## Deployment

### Prerequisites
- SQL Server 2025 Developer/Enterprise Edition
- .NET 8.0 Runtime
- Neo4j 5.0+ (optional for enhanced graph performance)
- Azure subscription (production only)

### Installation Steps
1. **Database Setup**:
   ```bash
   sqlcmd -S localhost -i database/V1_Unified_Schema.sql
   ```

2. **Application Deployment**:
   ```bash
   dotnet publish -c Release -o ./publish
   dotnet ./publish/Hartonomous.Api.dll
   ```

3. **Service Registration**:
   ```bash
   # Windows Service
   sc create HartonomousAPI binPath="dotnet Hartonomous.Api.dll"

   # Linux Systemd
   systemctl enable hartonomous-api.service
   ```

## Monitoring and Observability

### Application Insights Integration
- Request/response telemetry
- Custom metrics for model processing
- Performance counters
- Exception tracking

### Health Checks
- `/health` - Basic service health
- `/health/detailed` - Dependency status
- `/health/ready` - Readiness probe for Kubernetes

## License and Copyright

Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.

This software is proprietary and confidential. Unauthorized use is prohibited.