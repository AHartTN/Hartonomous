# Hartonomous AI Agent Factory Platform - Architecture Documentation

**Copyright (c) 2024-2025 All Rights Reserved. This software is proprietary and confidential. No part of this software may be reproduced, distributed, or transmitted in any form or by any means without the prior written permission of the copyright holder.**

## Executive Summary

The Hartonomous Platform is a comprehensive AI Agent Factory - "Shopify for AI Agents" - that enables users to create, deploy, and monetize specialized AI agents through advanced mechanistic interpretability and agent distillation techniques. The platform implements a novel unified data fabric architecture centered on SQL Server 2025's native AI capabilities.

## Core Architecture

### 1. Unified Data Fabric (NinaDB)

**Primary Technology**: SQL Server 2025 with native VECTOR data type
- **Vector Storage**: Native VECTOR(1536) columns with DiskANN indexing for semantic search
- **JSON Support**: Native JSON processing for flexible data structures
- **FILESTREAM Integration**: Binary large object storage for model weights and activation data
- **AI Functions**: Built-in AI_GENERATE_EMBEDDINGS and AI_GENERATE_CHUNKS functions

**Key Innovation**: SQL Server 2025 replaces traditional vector databases (Milvus) and provides:
- Up to 3,996 dimensions with half-precision floating-point support
- DiskANN-powered vector indexing for high-performance similarity search
- Native integration with Azure AI Foundry and OpenAI services
- TDS protocol enhancements for efficient vector data transmission

### 2. SQL CLR Integration Layer

**Technology**: .NET Framework SQL CLR assemblies
- **ActivationProcessor.cs**: Orchestrates ML model activation capture and storage
- **SkipTranscoderProcessor.cs**: Implements Skip Transcoder neural networks for interpretability
- **Neo4jCircuitBridge.cs**: Provides direct T-SQL to Neo4j integration for graph operations

**Capabilities**:
- Neural network training directly within SQL Server
- Memory-mapped file access for ultra-fast model querying
- Graph database synchronization without external message brokers
- Complex activation pattern analysis using advanced ML algorithms

### 3. Multi-Layered Service Architecture

#### Core Layer (Hartonomous.Core)
**Technology**: .NET 8, Entity Framework Core 8

**Key Services**:
- **AgentDistillationService**: Creates specialized agents from model components
- **MechanisticInterpretabilityService**: Analyzes neural patterns and discovers circuits
- **ConstitutionalAIService**: Implements safety constraints and ethical governance
- **ModelQueryEngineService**: Enables T-SQL queries against large language models
- **AgentRuntimeService**: Manages agent deployment across multiple environments

**Data Models**: 70+ Entity Framework models supporting multi-tenant architecture with comprehensive configuration classes

#### Infrastructure Layer
**Components**:
- **Neo4j Integration**: Graph database for relationship analysis and circuit discovery
- **Event Streaming**: Change Data Capture for real-time synchronization
- **Security**: Microsoft Entra ID authentication with JWT token validation
- **Observability**: Metrics collection, health checks, and distributed tracing

#### API Layer
**Technology**: ASP.NET Core Web APIs with OpenAPI/Swagger

**Services**:
- **Hartonomous.Api**: Main platform API
- **Hartonomous.ModelQuery**: Model introspection and neural map querying
- **Hartonomous.Orchestration**: Workflow execution and template management
- **Hartonomous.MCP**: Multi-Context Protocol for agent communication

#### Client Layer
**Technology**: .NET 8 SDK for thin client deployments

**Capabilities**:
- Agent loading and runtime management
- Task execution with dependency resolution
- Marketplace integration for agent discovery
- Telemetry and performance monitoring

### 4. Advanced ML Interpretability

#### Skip Transcoder Implementation
- **Architecture**: Encoder-decoder neural network for feature discovery
- **Training**: Gradient descent with Adam optimizer
- **Purpose**: Identifies interpretable features in model activations
- **Integration**: Executes within SQL Server for optimal performance

#### Circuit Discovery
- **Method**: Graph traversal algorithms in Neo4j
- **Analysis**: Causal relationship mapping between model components
- **Applications**: Agent distillation, safety analysis, capability extraction

#### Mechanistic Interpretability Pipeline
1. **Activation Capture**: Real-time model inference monitoring
2. **Feature Extraction**: Skip transcoder identifies meaningful patterns
3. **Circuit Analysis**: Graph algorithms discover computational pathways
4. **Agent Synthesis**: Distillation process creates specialized agents

### 5. Multi-Tenant Security Model

#### Authentication & Authorization
- **Microsoft Entra ID**: Enterprise identity management
- **JWT Tokens**: Stateless authentication with 'oid' claim for user identification
- **Multi-Tenant Isolation**: All data operations scoped by User ID
- **Azure Key Vault**: Secure secret management in production

#### Data Security
- **Row-Level Security**: Every database operation filtered by authenticated user
- **Parameterized Queries**: SQL injection prevention
- **Constitutional AI**: Runtime safety constraint enforcement
- **Audit Logging**: Comprehensive activity tracking

### 6. Deployment Architecture

#### Thin Client Design
- **Local Deployment**: Direct agent execution
- **Docker Containers**: Containerized agent instances
- **Kubernetes**: Orchestrated multi-agent deployments
- **Cloud Integration**: Azure, AWS, and GCP support

#### Scalability Features
- **Connection Pooling**: Efficient database resource utilization
- **Background Services**: Asynchronous processing pipelines
- **Caching**: Memory and distributed caching strategies
- **Load Balancing**: Multi-instance deployment support

## Technology Stack Summary

### Core Technologies
- **Database**: SQL Server 2025 with native vector capabilities
- **Framework**: .NET 8 / .NET Framework 4.8 (SQL CLR)
- **ORM**: Entity Framework Core 8
- **API**: ASP.NET Core Web APIs
- **Authentication**: Microsoft Identity Platform

### Infrastructure
- **Graph Database**: Neo4j for relationship analysis
- **Message Streaming**: Change Data Capture (CDC)
- **Caching**: Redis/SQL Server distributed caching
- **Monitoring**: Application Insights, custom metrics

### AI/ML Components
- **Vector Search**: SQL Server 2025 native VECTOR with DiskANN
- **ML Training**: Custom neural networks via SQL CLR
- **Embeddings**: Azure OpenAI / OpenAI integration
- **Model Storage**: FILESTREAM for binary model data

## Unique Architectural Innovations

1. **SQL Server as AI Platform**: Leveraging SQL Server 2025's native AI capabilities instead of external ML infrastructure
2. **In-Database ML Training**: Skip transcoder neural networks executing within SQL Server
3. **Unified Data Fabric**: Single source of truth eliminating data synchronization complexity
4. **Constitutional AI Integration**: Runtime safety constraints enforced at the database level
5. **Mechanistic Interpretability**: Advanced neural analysis for transparent AI agent creation

This architecture enables the "Shopify for AI Agents" vision by providing enterprise-grade infrastructure for AI agent creation, deployment, and monetization while maintaining full transparency through mechanistic interpretability techniques.