# Hartonomous Platform - Technical Reference Guide

## Platform Architecture

### Technology Foundation
Hartonomous is built on SQL Server 2025's native AI capabilities, utilizing the VECTOR data type and COSINE_DISTANCE functions for in-database neural network analysis. The platform follows Clean Architecture principles with clear separation between domain logic, application services, and infrastructure concerns.

### Core Technologies
- **.NET 8.0**: Modern C# application framework
- **SQL Server 2025**: Primary database with native vector operations
- **Neo4j**: Graph database for computational circuit relationships
- **Azure AD/Entra ID**: Enterprise authentication and authorization
- **Azure Key Vault**: Secure configuration and secrets management
- **SignalR**: Real-time WebSocket communication for agent coordination

## Project Structure and Dependencies

### Abstraction Layer
```
Hartonomous.DataFabric.Abstractions
├── IVectorService              # SQL Server vector operations interface
├── IGraphService               # Neo4j graph operations interface
└── IModelDataService           # FILESTREAM model access interface
```

### Core Domain
```
Hartonomous.Core
├── Models/                     # Domain entities with EF Core configurations
├── Services/                   # Business logic services
├── Repositories/               # Data access repositories
├── DTOs/                      # Data transfer objects
├── Interfaces/                # Service and repository contracts
└── Abstractions/              # Generic base classes and patterns
```

### Infrastructure Layer
```
Hartonomous.Infrastructure.SqlServer    # SQL Server 2025 vector implementation
Hartonomous.Infrastructure.Neo4j        # Neo4j graph database operations
Hartonomous.Infrastructure.Security     # Azure AD authentication
Hartonomous.Infrastructure.Configuration # Azure Key Vault integration
Hartonomous.Infrastructure.SqlClr       # SQL CLR assembly for model parsing
```

### Service Layer
```
Hartonomous.ModelService        # Unified model processing pipeline
Hartonomous.MCP                # Multi-Agent Context Protocol server
Hartonomous.Orchestration      # Workflow execution engine
```

### Presentation Layer
```
Hartonomous.Api                # REST API with JWT authentication
Hartonomous.AgentClient        # Runtime client for agent execution
```

## Data Architecture

### SQL Server 2025 Schema
```sql
-- Core model storage with FILESTREAM
CREATE TABLE FoundationModels (
    ModelId UNIQUEIDENTIFIER PRIMARY KEY,
    ModelName NVARCHAR(255) NOT NULL,
    ModelFormat NVARCHAR(50) NOT NULL,
    ModelData VARBINARY(MAX) FILESTREAM,
    UserId NVARCHAR(128) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);

-- Component embeddings with native VECTOR type
CREATE TABLE ComponentEmbeddings (
    ComponentId UNIQUEIDENTIFIER PRIMARY KEY,
    ModelId UNIQUEIDENTIFIER NOT NULL,
    EmbeddingVector VECTOR(1536) NOT NULL,
    ComponentType NVARCHAR(100) NOT NULL,
    UserId NVARCHAR(128) NOT NULL
);

-- Vector index for similarity search
CREATE INDEX IX_ComponentEmbeddings_Vector
ON ComponentEmbeddings(EmbeddingVector)
USING VECTOR;
```

### Neo4j Graph Schema
```cypher
// Model component nodes
CREATE (c:ModelComponent {
  id: "component-uuid",
  modelId: "model-uuid",
  name: "attention_layer_1",
  type: "attention",
  userId: "user-id"
});

// Computational circuit nodes
CREATE (circuit:Circuit {
  id: "circuit-uuid",
  name: "language_understanding",
  description: "Processes natural language inputs",
  userId: "user-id"
});

// Component relationships
CREATE (c1:ModelComponent)-[:FEEDS_INTO]->(c2:ModelComponent);
CREATE (comp:ModelComponent)-[:PART_OF]->(circuit:Circuit);
```

## Model Processing Pipeline

### 1. Model Ingestion
The ingestion process handles large foundation models using FILESTREAM storage and SQL CLR processing:

```sql
-- Store model with FILESTREAM
INSERT INTO FoundationModels (ModelId, ModelName, ModelFormat, ModelData, UserId)
VALUES (@ModelId, @ModelName, @Format, @ModelData, @UserId);

-- Parse architecture using SQL CLR
DECLARE @architecture NVARCHAR(MAX) = clr_ParseModelArchitecture(@ModelData, @Format);
```

### 2. Component Extraction
Neural network components are extracted and stored with vector embeddings:

```csharp
public async Task<ModelProcessingResult> ProcessModelAsync(ModelProcessingInput input)
{
    // Extract components using SQL CLR
    var components = await _modelDataService.ParseModelArchitectureAsync(input.ModelId, input.UserId);

    // Generate embeddings for components
    var embeddings = await GenerateComponentEmbeddingsAsync(components);

    // Store embeddings with vector indexing
    await _vectorService.BatchInsertEmbeddingsAsync(embeddings, input.UserId);

    return new ModelProcessingResult(input.ModelId, components, embeddings);
}
```

### 3. Circuit Discovery
Computational circuits are identified through graph analysis:

```csharp
public async Task<IEnumerable<ComputationalCircuitDto>> DiscoverCircuitsAsync(
    Guid modelId, IEnumerable<Guid> startingComponents, string userId)
{
    // Use Neo4j graph traversal to identify circuits
    return await _graphService.DiscoverCircuitsAsync(
        modelId, startingComponents, maxDepth: 5, minCircuitSize: 3, userId);
}
```

### 4. Agent Synthesis
Specialized agents are created using the Wanda algorithm for attribution-guided pruning:

```csharp
public async Task<AgentSynthesisResult> SynthesizeAgentAsync(AgentSynthesisRequest request)
{
    // Score component relevance for target domain
    var relevantComponents = await _attributionService.AnalyzeComponentRelevanceAsync(
        request.SourceModelId, request.TargetDomain, request.UserId);

    // Apply Wanda algorithm for pruning
    var prunedModel = await _distillationEngine.ApplyWandaPruningAsync(
        relevantComponents, request.PruningThreshold);

    // Create specialized agent configuration
    return await CreateAgentFromComponentsAsync(prunedModel, request.UserId);
}
```

## Security Implementation

### Multi-Tenant Data Isolation
All data access operations include automatic user scoping through generic repository patterns:

```csharp
public abstract class UserScopedRepository<T> : IUserScopedRepository<T>
    where T : class, IUserScopedEntity
{
    protected virtual IQueryable<T> GetUserScopedQuery(string userId)
    {
        return _dbSet.Where(e => e.UserId == userId);
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, string userId)
    {
        return await GetUserScopedQuery(userId)
            .FirstOrDefaultAsync(e => e.Id == id);
    }
}
```

### Authentication and Authorization
Azure AD integration provides enterprise-grade security:

```csharp
// JWT token validation middleware
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

// User ID extraction from claims
public static string GetUserId(this ClaimsPrincipal principal)
{
    return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
           throw new UnauthorizedAccessException("User ID not found in token");
}
```

## Performance Optimizations

### SQL Server 2025 Vector Operations
```sql
-- Optimized similarity search with vector indexing
SELECT TOP 100 ComponentName, RelevanceScore,
       VECTOR_DISTANCE('cosine', EmbeddingVector, @QueryVector) AS Distance
FROM ComponentEmbeddings WITH (INDEX(IX_ComponentEmbeddings_Vector))
WHERE UserId = @UserId
  AND VECTOR_DISTANCE('cosine', EmbeddingVector, @QueryVector) < @Threshold
ORDER BY Distance;
```

### Memory-Mapped Model Access
FILESTREAM enables processing of large models without loading into memory:

```csharp
public async Task<ModelFileHandle> GetModelFileHandleAsync(Guid modelId, string userId)
{
    // Return memory-mapped file handle for streaming access
    return new ModelFileHandle(modelId, filePath, fileSize, handle, format);
}
```

### Batch Processing Optimizations
```csharp
// Batch vector operations for performance
public async Task BatchInsertEmbeddingsAsync(
    IEnumerable<ComponentEmbeddingDto> embeddings, string userId)
{
    const int batchSize = 1000;
    var batches = embeddings.Chunk(batchSize);

    foreach (var batch in batches)
    {
        await InsertEmbeddingBatchAsync(batch, userId);
    }
}
```

## API Endpoints

### Model Management
- `POST /api/models` - Upload and process foundation models
- `GET /api/models/{id}` - Retrieve model metadata and analysis results
- `GET /api/models/{id}/components` - Get model components with embeddings
- `GET /api/models/{id}/circuits` - Retrieve discovered computational circuits
- `DELETE /api/models/{id}` - Remove model and associated data

### Agent Synthesis
- `POST /api/agents/synthesize` - Create specialized agent from model components
- `GET /api/agents` - List user's synthesized agents
- `GET /api/agents/{id}` - Get agent configuration and capabilities
- `POST /api/agents/{id}/deploy` - Deploy agent to MCP server
- `DELETE /api/agents/{id}` - Remove synthesized agent

### Component Queries
- `POST /api/query/components` - Semantic search over model components
- `POST /api/query/circuits` - Search computational circuits by functionality
- `POST /api/query/similarity` - Find similar components using vector search
- `GET /api/query/statistics` - Get model analysis statistics and metrics

## Configuration Management

### Development Environment
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=Hartonomous;Integrated Security=true;"
  },
  "Neo4j": {
    "Uri": "bolt://localhost:7687",
    "Username": "neo4j",
    "Password": "development"
  },
  "ModelService": {
    "MaxConcurrentProcessing": 3,
    "VectorDimensions": 1536,
    "ModelStoragePath": "./models"
  }
}
```

### Production Configuration
Production settings are retrieved from Azure Key Vault with automatic secret rotation:

```json
{
  "AzureAd": {
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

## Deployment Architecture

### Infrastructure Requirements
- **SQL Server 2025**: Enterprise or Developer Edition with VECTOR support
- **.NET 8.0 Runtime**: For application hosting
- **Neo4j 5.0+**: Community or Enterprise Edition for graph operations
- **Azure Subscription**: For production authentication and configuration

### Application Deployment
```bash
# Build and publish
dotnet publish -c Release -o ./publish --self-contained false

# Deploy to Azure App Service
az webapp deploy --resource-group hartonomous-rg --name hartonomous-api --src-path ./publish.zip

# Configure application settings
az webapp config appsettings set --resource-group hartonomous-rg --name hartonomous-api --settings @appsettings.json
```

### Database Deployment
```sql
-- Create database with FILESTREAM support
CREATE DATABASE Hartonomous
ON (NAME = 'Hartonomous_Data', FILENAME = 'C:\Data\Hartonomous.mdf'),
FILEGROUP FileStreamGroup CONTAINS FILESTREAM
(NAME = 'Hartonomous_FileStream', FILENAME = 'C:\Data\Hartonomous_FileStream');

-- Run schema deployment
sqlcmd -S server -d Hartonomous -i V1_Unified_Schema.sql
```

## Monitoring and Diagnostics

### Application Insights Integration
```csharp
// Custom telemetry for model processing
services.AddApplicationInsightsTelemetry();
services.AddScoped<IMetricsCollector, ApplicationInsightsMetricsCollector>();

// Track model processing metrics
public async Task TrackModelProcessingAsync(Guid modelId, TimeSpan duration, bool success)
{
    _telemetryClient.TrackEvent("ModelProcessed",
        properties: new Dictionary<string, string> { ["ModelId"] = modelId.ToString() },
        metrics: new Dictionary<string, double> { ["Duration"] = duration.TotalSeconds });
}
```

### Health Checks
```csharp
// Comprehensive health monitoring
services.AddHealthChecks()
    .AddSqlServer(connectionString)
    .AddNeo4j(neo4jConnectionString)
    .AddAzureKeyVault()
    .AddApplicationInsightsPublisher();
```

This technical reference provides comprehensive documentation for understanding, maintaining, and extending the Hartonomous platform architecture.