# Hartonomous - Autonomous AI Software Development Platform

## Project Overview

Hartonomous is an autonomous AI software development platform built on .NET 8 and SQL Server. It follows a clean architecture pattern, with a core project containing the main business logic and several other projects for infrastructure, services, and APIs. The platform is designed to be an autonomous AI software development agent, with features for project and model management, real-time communication, and orchestration.

The project is in a partial state of completion. The core foundation, including the database layer, security, and the main REST API, is complete and production-ready. However, several of the extended services have compilation and runtime errors.

## Building and Running

### Prerequisites

*   .NET 8.0 SDK
*   SQL Server 2025 (or compatible) with FILESTREAM enabled
*   Visual Studio 2022 or VS Code

### Building the Solution

The entire solution can be built using the following command:

```bash
dotnet build Hartonomous/Hartonomous.sln
```

### Running the Core Services

The core REST API can be run using the following command:

```bash
dotnet run --project Hartonomous/src/Api/Hartonomous.Api/Hartonomous.Api.csproj
```

### Running the Tests

The tests for the core services can be run using the following command:

```bash
dotnet test Hartonomous/tests/Hartonomous.Core.Tests/
dotnet test Hartonomous/tests/Hartonomous.Api.Tests/
```

### Database Deployment

The database schema can be deployed using the following command:

```bash
sqlcmd -S localhost -E -i Hartonomous/database/schema.sql
```

## Development Conventions

The project follows the principles of Clean Architecture and SOLID design patterns. It uses a consistent coding style and a clear project structure. The use of extension methods for service registration and configuration promotes a clean and maintainable codebase.

The project also includes a `CLAUDE.md` file, which provides development guidance for AI assistants. This file should be consulted for more detailed information on the project's development standards and patterns.
