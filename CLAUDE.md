# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is the Hartonomous Platform - a multi-tenant SaaS **AI Agent Factory** that enables users to create, deploy, and monetize specialized AI agents for any domain. Think "Shopify for AI Agents" - we provide the infrastructure, tools, and marketplace for the AI agent economy.

### Core Components

- **NinaDB**: SQL Server 2025 as AI-native NoSQL replacement with vector capabilities, native JSON, and FILESTREAM for model storage
- **Model Query Engine (MQE)**: "ESRI for AI Models" - ingest, index, and query large language models using T-SQL
- **Agent Factory**: Transform model capabilities into specialized, deployable agents (chess AI, customer service, domain experts)
- **Multi-Context Protocol (MCP)**: Agent communication and orchestration framework
- **Thin Client Architecture**: Deploy agents anywhere (cloud, edge, on-premises) without platform lock-in

The codebase is currently in implementation phase with comprehensive architectural documentation in `docs/`.

## Development Approach

When working on this project, you must follow the **Guiding Principles** outlined in the master development document:

### Core Problem-Solving Loop (Required for all tasks)
1. **Deconstruct:** Break tasks into smaller, logical sub-problems
2. **Explore Options:** Consider at least two valid alternatives in comments
3. **State Intent:** Declare your chosen approach and justify it based on project principles
4. **Implement:** Write clean, well-commented code
5. **Verify & Refine:** Self-critique for requirements, conventions, errors, security, and simplification

### Technical Standards
- **Security is Non-Negotiable:** All data access must be scoped by User ID (oid claim) from JWT token
- Use parameterized queries only
- Treat all input as untrustworthy
- Follow SOLID principles and DRY
- No placeholders or magic values - all code must be complete and functional
- Robust error handling is mandatory

## Project Structure Standards

- **Root Namespace:** Hartonomous
- **Project Naming:** Hartonomous.<Layer>.<Feature/Concern> (e.g., Hartonomous.Core, Hartonomous.Api.Ingestion)
- **Database Objects:** PascalCase (e.g., dbo.Projects)
- **Target Framework:** .NET 8 for new projects, .NET Framework 4.8 for SQL CLR

## Architecture Overview

The system follows a multi-layered, multi-tenant SaaS architecture:

- **NinaDB (Data Layer):** SQL Server 2025 with AI-native capabilities (vector indices, native JSON, FILESTREAM, SQL CLR)
- **Model Query Engine (MQE):** Core innovation enabling T-SQL queries against large language models
- **Core Layer:** Shared DTOs, business logic, and multi-tenant security (Hartonomous.Core)
- **Infrastructure Layer:** Security (JWT/Microsoft Identity), observability, event streaming (CDC)
- **API Layer:** RESTful services for model ingestion, agent creation, and marketplace operations
- **Agent Runtime:** Thin client architecture for deployable agents
- **UI Layer:** React-based agent factory interface and marketplace

## Key References

- **Technical Documentation:** Complete specs in `docs/` directory
- **Architecture Overview:** `docs/architecture/system-overview.md`
- **NinaDB Specifications:** `docs/architecture/ninadb-specifications.md`
- **MQE Documentation:** `docs/mge/mge-overview.md`
- **Business Strategy:** `docs/business/business-strategy.md`
- **Original Research:** `.reference-material/` (preserve, do not modify)

## Security Requirements

- Microsoft Entra ID (Azure AD) authentication
- JWT token validation with oid claim for user scoping
- All database operations must filter by authenticated user ID
- Least-privilege access patterns throughout

## Development Commands

This project is in initialization phase. When implementing:

1. Start with the database schema (Module 2)
2. Build shared libraries (Module 3)
3. Implement API services (future modules)
4. Add React frontend (future modules)

Follow the sequential steps in the master development document for proper implementation order.