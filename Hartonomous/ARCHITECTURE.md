# Hartonomous Platform - Technical Architecture

## System Overview

Hartonomous implements a clean architecture pattern with SQL Server 2025 as the primary data engine, leveraging native VECTOR operations for neural network analysis. The platform eliminates external vector database dependencies by utilizing SQL Server's AI-enhanced capabilities for in-database model processing.

## Architectural Principles

### Database-Centric Design
- **SQL Server 2025**: Primary storage with native vector operations using VECTOR data type
- **FILESTREAM Integration**: Memory-mapped access to large model files without RAM limitations
- **SQL CLR Processing**: High-performance model parsing and analysis within database context
- **User-Scoped Multi-Tenancy**: All operations automatically isolated by UserId at database level

### Service-Oriented Architecture
- **Unified ModelService**: Single pipeline for model ingestion, analysis, and agent synthesis
- **DataFabric Abstractions**: Clean interface separation between business logic and infrastructure
- **Domain-Driven Design**: Core business logic isolated from infrastructure concerns
- **Dependency Inversion**: Infrastructure implementations hidden behind abstractions

## System Components

### Core Services

#### Hartonomous.ModelService
**Purpose**: Unified neural processing pipeline
**Responsibilities**:
- Model ingestion (GGUF, SafeTensors)
- Circuit discovery via mechanistic interpretability
- Attribution analysis using Wanda algorithm
- Agent synthesis and optimization

**Key Components**:
```
ModelService/
├── Services/
│   ├── ModelProcessingService      # GGUF/SafeTensors parsing
│   ├── CircuitDiscoveryService     # Mechanistic interpretability
│   ├── AttributionAnalysisService  # Wanda algorithm implementation
│   └── AgentSynthesisService       # Specialized agent creation
├── Repositories/
│   ├── ModelRepository             # Foundation model storage
│   ├── ComponentRepository         # Neural component management
│   └── CircuitRepository           # Discovered circuit storage
└── Abstractions/
    ├── IModelProcessor<T>          # Generic model processing
    ├── ICircuitDiscoverer          # Circuit discovery interface
    └── IAttributionAnalyzer        # Attribution analysis interface
```

#### Hartonomous.Core
**Purpose**: Domain models and shared business logic
**Responsibilities**:
- Entity definitions and business rules
- Cross-cutting concerns and utilities
- Validation and domain services

#### Hartonomous.MCP
**Purpose**: Multi-Agent Context Protocol server
**Responsibilities**:
- Real-time agent communication via SignalR
- Workflow orchestration and task assignment
- Agent registration and discovery

### Infrastructure Layer

#### Hartonomous.Infrastructure.SqlServer
**Purpose**: SQL Server 2025 native operations
**Key Features**:
- VECTOR data type operations
- COSINE_DISTANCE similarity calculations
- SQL CLR assembly integration
- Optimized queries for neural analysis

#### Hartonomous.Infrastructure.Neo4j
**Purpose**: Graph database for circuit relationships
**Use Cases**:
- Complex circuit traversal queries
- Relationship discovery between components
- Performance optimization for graph operations

#### Hartonomous.Infrastructure.Security
**Purpose**: Azure AD/Entra ID integration
**Features**:
- JWT token validation
- Multi-tenant user isolation
- Role-based access control

#### Hartonomous.Infrastructure.Configuration
**Purpose**: Azure Key Vault integration
**Features**:
- Secure secret management
- Environment-specific configuration
- Development/production isolation

## Data Architecture

### Primary Storage: SQL Server 2025

#### Core Tables
```sql
-- Foundation models with FILESTREAM storage
CREATE TABLE FoundationModels (
    ModelId UNIQUEIDENTIFIER PRIMARY KEY,
    ModelName NVARCHAR(255) NOT NULL,
    ModelData VARBINARY(MAX) FILESTREAM,
    UserId NVARCHAR(128) NOT NULL
);

-- Model components with vector embeddings
CREATE TABLE ModelComponents (
    ComponentId UNIQUEIDENTIFIER PRIMARY KEY,
    ModelId UNIQUEIDENTIFIER NOT NULL,
    ComponentName NVARCHAR(255),
    ComponentType NVARCHAR(100),
    EmbeddingVector VECTOR(1536) NOT NULL,
    RelevanceScore FLOAT,
    UserId NVARCHAR(128) NOT NULL
);

-- Discovered computational circuits
CREATE TABLE ComputationalCircuits (
    CircuitId UNIQUEIDENTIFIER PRIMARY KEY,
    ModelId UNIQUEIDENTIFIER NOT NULL,
    CircuitName NVARCHAR(255),
    FunctionalDescription NVARCHAR(MAX),
    ImportanceScore FLOAT,
    UserId NVARCHAR(128) NOT NULL
);

-- Synthesized agents
CREATE TABLE DistilledAgents (
    AgentId UNIQUEIDENTIFIER PRIMARY KEY,
    AgentName NVARCHAR(255) NOT NULL,
    SourceModelId UNIQUEIDENTIFIER,
    AgentConfiguration NVARCHAR(MAX), -- JSON
    UserId NVARCHAR(128) NOT NULL
);
```

#### Vector Operations
```sql
-- Semantic search over model components
SELECT ComponentName, RelevanceScore
FROM ModelComponents
WHERE VECTOR_DISTANCE('cosine', EmbeddingVector, @QueryVector) < 0.3
  AND UserId = @UserId
ORDER BY VECTOR_DISTANCE('cosine', EmbeddingVector, @QueryVector);

-- Find similar circuits
SELECT c1.CircuitName, c2.CircuitName,
       VECTOR_DISTANCE('cosine', c1.EmbeddingVector, c2.EmbeddingVector) AS Similarity
FROM ComputationalCircuits c1
CROSS JOIN ComputationalCircuits c2
WHERE c1.CircuitId != c2.CircuitId
  AND c1.UserId = @UserId
  AND c2.UserId = @UserId;
```

### Secondary Storage: Neo4j

#### Graph Schema
```cypher
// Model component nodes
CREATE (c:ModelComponent {
  id: "component-guid",
  modelId: "model-guid",
  name: "attention_head_1",
  type: "attention",
  userId: "user-id"
})

// Circuit nodes
CREATE (circuit:Circuit {
  id: "circuit-guid",
  name: "natural_language_understanding",
  description: "Processes natural language inputs",
  userId: "user-id"
})

// Relationships
CREATE (c1:ModelComponent)-[:FEEDS_INTO]->(c2:ModelComponent)
CREATE (comp:ModelComponent)-[:PART_OF]->(circuit:Circuit)
CREATE (circuit1:Circuit)-[:DEPENDS_ON]->(circuit2:Circuit)
```

## Processing Pipeline

### Model Ingestion Pipeline
1. **Upload**: Foundation model (GGUF/SafeTensors) uploaded to FILESTREAM
2. **Parse**: SQL CLR assembly extracts architecture and weights
3. **Analyze**: Components identified and stored with metadata
4. **Embed**: Component embeddings generated and stored as VECTOR
5. **Index**: Vector indexes created for similarity search

### Circuit Discovery Pipeline
1. **Activation Analysis**: SQL CLR processes model activations
2. **Pattern Detection**: Mechanistic interpretability algorithms identify circuits
3. **Graph Storage**: Circuit relationships stored in Neo4j
4. **Validation**: Circuits validated through attribution analysis

### Agent Synthesis Pipeline
1. **Component Selection**: Wanda algorithm scores component importance
2. **Circuit Assembly**: Relevant circuits combined for target domain
3. **Agent Creation**: Specialized agent synthesized from components
4. **Deployment**: Agent registered with MCP for orchestration

## SQL CLR Integration

### Model Processing Functions
```sql
-- Parse model architecture from FILESTREAM data
DECLARE @architecture NVARCHAR(MAX) = clr_ParseModelArchitecture(@ModelData, @ModelFormat);

-- Extract component activations
EXEC clr_ExtractActivations
    @ModelId = @ModelId,
    @InputData = @InputData,
    @UserId = @UserId;

-- Perform attribution analysis
EXEC clr_AttributionAnalysis
    @ModelId = @ModelId,
    @TargetComponents = @ComponentIds,
    @UserId = @UserId;
```

### Performance Optimizations
- Memory-mapped model access via FILESTREAM
- Batch processing for component analysis
- Parallel execution for large models
- Optimized T-SQL queries for neural operations

## Security Architecture

### Multi-Tenant Isolation
- All database operations include UserId filtering
- User-scoped vector searches and circuit discovery
- Isolated agent synthesis and deployment

### Authentication Flow
1. Client authenticates with Azure AD
2. JWT token issued with user claims
3. API validates token and extracts UserId
4. All operations scoped to authenticated user

### Data Protection
- Azure Key Vault for connection strings and secrets
- TLS encryption for all communications
- Audit logging for all model operations
- Constitutional AI constraints for safety

## Deployment Architecture

### Development Environment
- SQL Server 2025 Developer Edition
- Local configuration files
- In-memory authentication
- Neo4j Community Edition (optional)

### Production Environment
- Azure SQL Database with vector support
- Azure App Service for API hosting
- Azure Key Vault for secrets
- Azure AD for authentication
- Application Insights for monitoring

## Performance Characteristics

### SQL Server 2025 Optimizations
- Native vector indexing: O(log n) similarity search
- SQL CLR processing: Near-native C# performance
- FILESTREAM storage: Optimal for large model files
- Connection pooling: Efficient resource utilization

### Scalability Considerations
- Horizontal scaling via read replicas
- Partitioning large models across multiple databases
- Caching frequently accessed components
- Asynchronous processing for long-running operations

## Integration Points

### External Systems
- **Azure AD**: Enterprise identity provider
- **Application Insights**: Telemetry and monitoring
- **Azure Key Vault**: Secret management
- **GitHub**: Source code and CI/CD

### API Interfaces
- **REST API**: Standard HTTP operations
- **SignalR**: Real-time agent communication
- **GraphQL**: Flexible query interface (future)
- **gRPC**: High-performance service communication (future)

## Future Enhancements

### SQL Server 2025 Advanced Features
- Additional vector distance metrics
- Enhanced CLR capabilities
- Native AI model serving
- Improved vector indexing

### Platform Extensions
- Model marketplace integration
- Advanced constitutional AI
- Distributed agent deployment
- Real-time model adaptation