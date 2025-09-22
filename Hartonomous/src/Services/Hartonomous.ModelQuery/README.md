# Hartonomous Model Query Engine

## Technical Overview

The Model Query Engine provides T-SQL queryable access to neural network components through the "Neural Map" concept. It implements semantic search and retrieval operations over model architectures, weights, and component relationships using SQL Server 2025's native vector capabilities.

## Overview

This service provides a comprehensive API for:
- **Neural Map Management**: Creating and managing graph-based representations of neural networks
- **Model Weight Management**: Storing and retrieving model weights with metadata
- **Model Introspection**: Analyzing model architecture and capabilities
- **Semantic Search**: Searching across model components semantically
- **Model Versioning**: Managing model versions and comparisons
- **Model Architecture**: Managing layer configurations and structures

## Key Features

### 1. Neural Map (Graph Database)
- Treats neural network components as nodes in a graph
- Stores relationships between layers, weights, and operations
- Uses Neo4j for graph-based queries and relationships
- Supports semantic traversal of model structures

### 2. Model Weight Storage
- FileStream-based storage for large weight files
- Metadata tracking (checksums, shapes, data types)
- User-scoped access control
- Efficient streaming for large model weights

### 3. Model Introspection
- Automatic analysis of model capabilities
- Parameter counting and statistics
- Layer type analysis
- Framework detection

### 4. Semantic Search
- Search across model components by description
- Similarity-based retrieval
- Support for different search types (layers, weights, nodes)

## API Endpoints

### Neural Map Controller (`/api/NeuralMap`)

#### Get Model Graph
```http
GET /api/NeuralMap/models/{modelId}/graph
```
Returns the complete neural map graph for a model.

**Response**: `NeuralMapGraphDto`
```json
{
  "modelId": "guid",
  "modelName": "string",
  "version": "string",
  "nodes": [...],
  "edges": [...],
  "metadata": {}
}
```

#### Get Nodes
```http
GET /api/NeuralMap/models/{modelId}/nodes
```
Returns all nodes for a model.

#### Get Edges
```http
GET /api/NeuralMap/models/{modelId}/edges
```
Returns all edges for a model.

#### Create Node
```http
POST /api/NeuralMap/models/{modelId}/nodes
```
**Body**:
```json
{
  "nodeType": "layer|operation|weight",
  "name": "string",
  "properties": {}
}
```

#### Create Edge
```http
POST /api/NeuralMap/models/{modelId}/edges
```
**Body**:
```json
{
  "sourceNodeId": "guid",
  "targetNodeId": "guid",
  "relationType": "connects|feeds_into|depends_on",
  "weight": 0.0,
  "properties": {}
}
```

### Model Introspection Controller (`/api/ModelIntrospection`)

#### Analyze Model
```http
GET /api/ModelIntrospection/models/{modelId}/analyze
```
Performs comprehensive model analysis.

**Response**: `ModelIntrospectionDto`
```json
{
  "modelId": "guid",
  "modelName": "string",
  "totalParameters": 1000000,
  "trainableParameters": 1000000,
  "modelSizeMB": 4.5,
  "layerTypeCount": {
    "Dense": 3,
    "Conv2D": 2
  },
  "statistics": {},
  "capabilities": ["computer_vision", "fully_connected"],
  "analyzedAt": "2025-01-15T10:30:00Z"
}
```

#### Semantic Search
```http
POST /api/ModelIntrospection/search
```
**Body**: `SemanticSearchRequestDto`
```json
{
  "query": "dense layer with relu activation",
  "searchType": "layers|weights|nodes|all",
  "maxResults": 10,
  "similarityThreshold": 0.7,
  "filters": {}
}
```

#### Get Model Statistics
```http
GET /api/ModelIntrospection/models/{modelId}/statistics
```
Returns detailed model statistics.

#### Get Model Capabilities
```http
GET /api/ModelIntrospection/models/{modelId}/capabilities
```
Returns detected model capabilities.

#### Compare Models
```http
POST /api/ModelIntrospection/models/compare
```
**Body**:
```json
{
  "modelAId": "guid",
  "modelBId": "guid",
  "comparisonType": "architecture|parameters|capabilities"
}
```

### Model Weights Controller (`/api/ModelWeights`)

#### Get Model Weights
```http
GET /api/ModelWeights/models/{modelId}
```
Returns all weights for a model.

#### Get Weight by ID
```http
GET /api/ModelWeights/{weightId}
```
Returns specific weight metadata.

#### Get Weights by Layer
```http
GET /api/ModelWeights/models/{modelId}/layers/{layerName}
```
Returns all weights for a specific layer.

#### Create Weight
```http
POST /api/ModelWeights/models/{modelId}
```
**Body**:
```json
{
  "layerName": "string",
  "weightName": "string",
  "dataType": "float32|float16|int8",
  "shape": [10, 5, 3],
  "sizeBytes": 600,
  "storagePath": "model123/layer1/weights.bin",
  "checksumSha256": "sha256hash"
}
```

#### Download Weight Data
```http
GET /api/ModelWeights/{weightId}/data
```
Downloads the binary weight data.

#### Upload Weight Data
```http
POST /api/ModelWeights/{weightId}/data
```
Uploads binary weight data (multipart/form-data).

### Model Versions Controller (`/api/ModelVersions`)

#### Get Model Versions
```http
GET /api/ModelVersions/models/{modelId}
```
Returns all versions for a model.

#### Get Latest Version
```http
GET /api/ModelVersions/models/{modelId}/latest
```
Returns the latest version of a model.

#### Create Version
```http
POST /api/ModelVersions/models/{modelId}
```
**Body**:
```json
{
  "version": "1.2.0",
  "description": "Added attention mechanism",
  "changes": {
    "layers_added": ["attention_1", "attention_2"],
    "layers_modified": ["dense_output"]
  },
  "parentVersion": "1.1.0"
}
```

#### Compare Versions
```http
POST /api/ModelVersions/compare
```
**Body**:
```json
{
  "versionAId": "guid",
  "versionBId": "guid"
}
```

## Configuration

### appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=Hartonomous;Trusted_Connection=true;"
  },
  "Neo4j": {
    "Uri": "bolt://192.168.1.2:7687",
    "Username": "neo4j",
    "Password": "your-password"
  },
  "ModelStorage": {
    "FileStreamPath": "C:\\ModelStorage\\Weights"
  },
  "Jwt": {
    "Key": "your-jwt-signing-key",
    "Issuer": "Hartonomous.ModelQuery",
    "Audience": "Hartonomous.ModelQuery.Api"
  }
}
```

### Database Setup

1. **SQL Server**: Run the schema script `Database/ModelQuerySchema.sql`
2. **Neo4j**: Ensure Neo4j is running and accessible
3. **File Storage**: Create the FileStreamPath directory

## Authentication

All endpoints require JWT authentication. Include the JWT token in the Authorization header:
```
Authorization: Bearer <your-jwt-token>
```

## Data Models

### Core DTOs

- **NeuralMapNodeDto**: Represents a node in the neural map graph
- **NeuralMapEdgeDto**: Represents a connection between nodes
- **ModelWeightDto**: Metadata for model weights
- **ModelLayerDto**: Layer configuration and associated weights
- **ModelArchitectureDto**: Complete model architecture
- **ModelIntrospectionDto**: Analysis results for a model
- **SemanticSearchResultDto**: Results from semantic search

## Error Handling

Standard HTTP status codes:
- **200**: Success
- **400**: Bad Request (validation errors)
- **401**: Unauthorized (missing/invalid JWT)
- **403**: Forbidden (access denied)
- **404**: Not Found (resource doesn't exist)
- **500**: Internal Server Error

## Usage Examples

### Analyzing a Model
```bash
curl -X GET "https://api.hartonomous.com/api/ModelIntrospection/models/123e4567-e89b-12d3-a456-426614174000/analyze" \
  -H "Authorization: Bearer your-jwt-token"
```

### Semantic Search
```bash
curl -X POST "https://api.hartonomous.com/api/ModelIntrospection/search" \
  -H "Authorization: Bearer your-jwt-token" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "convolutional layers with batch normalization",
    "searchType": "layers",
    "maxResults": 5,
    "similarityThreshold": 0.8
  }'
```

### Creating a Neural Map Node
```bash
curl -X POST "https://api.hartonomous.com/api/NeuralMap/models/123e4567-e89b-12d3-a456-426614174000/nodes" \
  -H "Authorization: Bearer your-jwt-token" \
  -H "Content-Type: application/json" \
  -d '{
    "nodeType": "layer",
    "name": "conv2d_1",
    "properties": {
      "filters": 32,
      "kernelSize": [3, 3],
      "activation": "relu"
    }
  }'
```

## Performance Considerations

- **Weight Storage**: Large weights are stored on disk and streamed
- **Graph Queries**: Neo4j provides efficient graph traversal
- **Caching**: Consider implementing caching for frequently accessed models
- **Indexing**: Database indexes are created for optimal query performance

## Security

- **User Scoping**: All operations are scoped to the authenticated user
- **SQL Injection**: Uses parameterized queries (Dapper)
- **File Access**: FileStream paths are validated and scoped
- **JWT Validation**: Proper token validation and expiration

## Testing

Run the test suite:
```bash
cd tests/Hartonomous.ModelQuery.Tests
dotnet test
```

Tests cover:
- Service layer logic
- Controller endpoints
- Repository patterns
- DTO serialization

## Deployment

1. **Database Migration**: Apply SQL schema
2. **Neo4j Setup**: Configure graph database
3. **File Storage**: Setup storage directory
4. **Configuration**: Set connection strings and JWT keys
5. **Deploy**: Standard ASP.NET Core deployment

## Future Enhancements

- **Vector Embeddings**: Add semantic embeddings for better search
- **Model Diffing**: Enhanced model comparison algorithms
- **Performance Metrics**: Runtime performance tracking
- **Model Optimization**: Suggest optimization opportunities
- **Export/Import**: Model serialization and migration tools