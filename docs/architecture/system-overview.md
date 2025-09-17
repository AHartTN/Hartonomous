# Hartonomous Platform: System Architecture Overview

**The Multi-Tenant AI Agent Factory Platform**

## Executive Summary

The Hartonomous Platform is a comprehensive SaaS solution for creating, deploying, and monetizing specialized AI agents. Built on a foundation of SQL Server 2025's AI-native capabilities, the platform enables users to transform large language models into domain-specific agents for any industry or use case.

## Core Architecture Principles

### **1. Multi-Tenant Security-First Design**
- **User Isolation**: Azure AD + Entra External ID for identity federation
- **Data Sovereignty**: All operations scoped by User ID (oid claim)
- **Tenant Separation**: Logical isolation in shared infrastructure
- **Enterprise Integration**: SSO and on-premises deployment options

### **2. Agent Factory Pattern**
- **Model Ingestion**: MQE processes large models into queryable components
- **Capability Discovery**: T-SQL queries to find specific model abilities
- **Agent Distillation**: Extract and combine capabilities into specialized agents
- **Deployment Flexibility**: Thin clients deployable anywhere

### **3. Unified Data Fabric**
- **Single Source of Truth**: SQL Server 2025 as immutable system of record
- **Event-Driven Replication**: Real-time CDC to specialized data stores
- **Zero-Copy Performance**: Memory-mapped file access for model operations
- **Transactional Consistency**: ACID guarantees across all operations

## System Components

```
┌─────────────────────────────────────────────────────────────────┐
│                    Hartonomous Platform                         │
├─────────────────────────────────────────────────────────────────┤
│  Frontend Layer                                                 │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Agent Studio   │  │  Model Explorer │  │  Marketplace    │  │
│  │  (Creator UI)   │  │  (MQE Interface)│  │  (Agent Store)  │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│  API Gateway Layer                                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Agent Factory  │  │  Model Query    │  │  Marketplace    │  │
│  │  API Service    │  │  Engine (MQE)   │  │  API Service    │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│  Core Platform Services                                         │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  MCP Protocol   │  │  Orchestration  │  │  Agent Runtime  │  │
│  │  (Multi-Context)│  │  Service        │  │  Environment    │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│  Infrastructure Services                                        │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Security &     │  │  Observability  │  │  Configuration  │  │
│  │  Identity       │  │  & Monitoring   │  │  Management     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│  Data Layer (NinaDB)                                           │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                SQL Server 2025                             │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐   │ │
│  │  │   Vector    │ │    JSON     │ │     FILESTREAM      │   │ │
│  │  │   Indices   │ │   Storage   │ │   (Model Chunks)    │   │ │
│  │  │   (HNSW)    │ │  (NoSQL)    │ │   (Peek/Seek)       │   │ │
│  │  └─────────────┘ └─────────────┘ └─────────────────────┘   │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐   │ │
│  │  │    Graph    │ │   SQL CLR   │ │    Change Data      │   │ │
│  │  │ Capabilities│ │  Interop    │ │     Capture         │   │ │
│  │  │ (Relationships)│ │(Memory Map)│ │   (Real-time)      │   │ │
│  │  └─────────────┘ └─────────────┘ └─────────────────────┘   │ │
│  └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

## Data Flow Architecture

### **1. Model Ingestion Pipeline**
```
Large Model → MQE Ingestion → Vector Analysis → NinaDB Storage
     ↓              ↓              ↓              ↓
   Llama4     Chunking &     HNSW Indexing   FILESTREAM +
  Maverick    Embedding     Semantic Map     JSON Metadata
```

### **2. Agent Creation Workflow**
```
User Request → Capability Query → Model Discovery → Agent Assembly
     ↓              ↓               ↓               ↓
"Chess Agent"  T-SQL Semantic   Find Strategy   Combine Components
               Search           Components      → Deploy Agent
```

### **3. Multi-Tenant Data Isolation**
```
User Request → JWT Validation → User ID Extraction → Scoped Queries
     ↓              ↓               ↓                ↓
All API Calls   oid claim      userId Parameter   WHERE UserId = @userId
```

## Key Innovations

### **1. Memory-Mapped Model Access**
- **Challenge**: Loading 70B+ parameter models into memory is prohibitive
- **Solution**: FILESTREAM + SQL CLR for sub-millisecond peek/seek operations
- **Benefit**: Query specific model sections without full model loading

### **2. T-SQL Model Querying**
- **Challenge**: Models are opaque binary files
- **Solution**: Vector indices + semantic mapping enable SQL-like queries
- **Benefit**: `SELECT capabilities WHERE domain = 'chess' AND skill_level > 0.8`

### **3. Thin Client Architecture**
- **Challenge**: Agents need deployment flexibility (cloud, edge, on-premises)
- **Solution**: Lightweight runtime that connects to platform APIs
- **Benefit**: Same agent framework works across all deployment scenarios

### **4. Constitutional AI Safety**
- **Challenge**: Autonomous agents need ethical constraints
- **Solution**: Immutable safety rules encoded at the platform level
- **Benefit**: All produced agents inherit safety guarantees

## Scalability & Performance

### **Horizontal Scaling**
- **API Services**: Stateless microservices with load balancing
- **Database Layer**: SQL Server read replicas for query distribution
- **Agent Runtime**: Containerized deployment with Kubernetes orchestration

### **Vertical Optimization**
- **Vector Operations**: HNSW indices for O(log n) similarity search
- **Memory Efficiency**: Memory-mapped files prevent RAM exhaustion
- **Query Performance**: Optimized T-SQL with indexed JSON columns

### **Multi-Tenancy**
- **Data Isolation**: Logical separation with userId scoping
- **Resource Quotas**: Per-tenant limits on storage, compute, API calls
- **Billing Integration**: Usage tracking for accurate cost allocation

## Security Architecture

### **Identity & Access Management**
- **Azure AD Integration**: Enterprise SSO and conditional access
- **Entra External ID**: B2B identity federation for partners
- **Role-Based Access Control**: Granular permissions per user/tenant

### **Data Protection**
- **Encryption at Rest**: SQL Server TDE for database encryption
- **Encryption in Transit**: TLS 1.3 for all API communications
- **Key Management**: Azure Key Vault integration for secrets

### **Agent Sandboxing**
- **Containerized Execution**: Docker isolation for agent runtime
- **Network Policies**: Restricted egress for security
- **Audit Logging**: Comprehensive activity tracking

## Deployment Architecture

### **Cloud-Native (Primary)**
- **Azure App Service**: Managed hosting for API services
- **Azure SQL Database**: Managed database with vector capabilities
- **Azure Container Instances**: Agent runtime hosting

### **Hybrid Deployment**
- **Azure Arc**: Unified management across environments
- **On-Premises Agents**: Enterprise data sovereignty requirements
- **Edge Computing**: Local agent execution with cloud coordination

### **Development Environment**
- **Docker Compose**: Local development stack
- **Visual Studio**: Integrated debugging and deployment
- **Azure DevOps**: CI/CD pipeline automation

---

*This architecture overview provides the foundational understanding for detailed component documentation. See individual service documentation for implementation specifics.*