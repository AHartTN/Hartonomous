# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is the Hartonomous project - an autonomous AI software development agent platform. The codebase is currently in the planning/initialization phase with comprehensive architectural documentation stored in `.reference-material/`.

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

The planned system follows a multi-layered architecture:

- **Database Layer:** SQL Server with FILESTREAM, graph capabilities, and CLR integration (HartonomousDB)
- **Core Layer:** Shared DTOs and business logic (Hartonomous.Core)
- **Infrastructure Layer:** Security (JWT/Microsoft Identity), Observability (OpenTelemetry)
- **API Layer:** RESTful services for ingestion, querying, and orchestration
- **UI Layer:** React-based frontend

## Key References

- Master development steps: `.reference-material/Hartonomous Project_ Master Development Steps (Zero-Context Edition v3).md`
- Detailed research and architectural plans available in `.reference-material/Research/`

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