# Hartonomous - Autonomous AI Software Development Platform

**Status:** Foundation Complete (60% of Original Specification)
**Build Status:** ⚠️ Compilation Issues in Extended Modules
**Core Foundation:** ✅ Working and Production Ready

## Project Overview

Hartonomous is an autonomous AI software development platform built around the "NinaDB" philosophy - using SQL Server as a single source of truth with specialized read replicas for advanced querying capabilities.

## Current Status

### ✅ **COMPLETED (Production Ready)**

#### Core Foundation (Modules 1-3)
- **Database Layer:** SQL Server with FILESTREAM for model storage ✅
- **Security:** User-scoped JWT authentication throughout ✅
- **Architecture:** Clean Architecture with SOLID principles ✅
- **API:** Complete REST API for projects and models ✅
- **Testing:** Comprehensive unit tests (100% pass rate) ✅

### ⚠️ **ISSUES TO RESOLVE**

#### Build Problems
- **52 compilation errors** in extended modules
- Missing basic using statements (System.IO, etc.)
- Package version conflicts in test projects

#### Runtime Issues
- JWT configuration missing for development
- Services start but fail on authentication

### 📋 **NOT YET IMPLEMENTED (Per Original Spec)**

#### Data Fabric Integration
- Neo4j integration (read replica for relationships)
- Milvus integration (vector search for semantics)
- Kafka/Debezium CDC pipeline
- Complete Model Query Engine (LLM weight querying)

## Architecture

### Current Implementation
```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   SQL Server    │    │   REST API       │    │   Client Apps   │
│   (Source of    │◄──►│   (Authentication│◄──►│   (Future)      │
│    Truth)       │    │    & Business    │    │                 │
└─────────────────┘    │    Logic)        │    └─────────────────┘
                       └──────────────────┘
```

### Target Architecture (Original Spec)
```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   SQL Server    │    │      Kafka       │    │     Neo4j       │
│   (Source of    │◄──►│   (Event Bus)    │◄──►│ (Relationships) │
│    Truth)       │    └──────────────────┘    └─────────────────┘
└─────────────────┘              │             ┌─────────────────┐
         │                       └─────────────┤     Milvus      │
         │                                     │   (Vectors)     │
         ▼                                     └─────────────────┘
┌─────────────────┐    ┌──────────────────┐
│   SQL CLR       │    │   Model Query    │
│ (LLM Querying)  │◄──►│     Engine       │
└─────────────────┘    └──────────────────┘
```

## Quick Start

### Prerequisites
- SQL Server 2025 (or compatible) with FILESTREAM enabled
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code

### Running the Foundation
```bash
# Clone and navigate
git clone <repository>
cd Hartonomous

# Build foundation (works)
dotnet build src/Core/Hartonomous.Core/
dotnet build src/Api/Hartonomous.Api/

# Run tests (pass)
dotnet test tests/Hartonomous.Core.Tests/
dotnet test tests/Hartonomous.Api.Tests/

# Deploy database
sqlcmd -S localhost -E -i database/schema.sql
```

### ⚠️ Known Issues with Extended Services
```bash
# These have compilation errors:
dotnet build src/Services/Hartonomous.MCP/          # ❌ 15 errors
dotnet build src/Services/Hartonomous.ModelQuery/   # ❌ 8 errors
dotnet build src/Services/Hartonomous.Orchestration/ # ❌ 12 errors
dotnet build tests/Hartonomous.AgentClient.Tests/   # ❌ 17 errors
```

## Project Structure

```
Hartonomous/
├── src/
│   ├── Api/                    # ✅ REST API (working)
│   ├── Core/                   # ✅ DTOs, repositories (working)
│   ├── Database/               # ✅ SQL CLR assembly (working)
│   ├── Infrastructure/         # ✅ Security, config, observability (working)
│   ├── Services/               # ⚠️ Extended services (build issues)
│   └── Client/                 # ⚠️ Agent client (build issues)
├── tests/                      # ✅ Foundation tests pass
├── database/                   # ✅ Deployed successfully
├── PROJECT_STATUS_REPORT.md    # 📊 Honest assessment
├── ROADMAP.md                  # 🗺️ Path forward
└── README.md                   # 📖 This file
```

## Documentation

- **[Project Status Report](PROJECT_STATUS_REPORT.md)** - Honest assessment of current state
- **[Roadmap](ROADMAP.md)** - Planned work to complete original specification
- **[CLAUDE.md](CLAUDE.md)** - Development guidance for AI assistants

## Key Features (Implemented)

### ✅ Database Foundation
- Multi-tenant with user-scoped security
- FILESTREAM support for large model files
- Graph database concepts with SQL Server nodes/edges
- JSON indexing for flexible metadata

### ✅ API Layer
- JWT authentication with Microsoft Identity
- User-scoped data access throughout
- RESTful endpoints for projects and models
- Comprehensive error handling and validation

### ✅ Infrastructure
- OpenTelemetry observability
- Azure Key Vault configuration
- Clean Architecture with dependency injection
- Comprehensive logging and metrics

## Next Steps

Based on the [Roadmap](ROADMAP.md), the immediate priorities are:

1. **Fix Build Issues** - Resolve 52 compilation errors
2. **Service Authentication** - Configure JWT for development
3. **Complete Original Spec** - Neo4j, Milvus, Kafka integration
4. **Model Query Engine** - Implement LLM weight querying

## Contributing

The project follows Clean Architecture principles with SOLID design patterns. See [CLAUDE.md](CLAUDE.md) for development standards and patterns.

## License

[License details to be added]

---

**Note:** This README reflects the honest current state. The foundation is solid and production-ready, but extended modules need stabilization work before they can be considered functional.