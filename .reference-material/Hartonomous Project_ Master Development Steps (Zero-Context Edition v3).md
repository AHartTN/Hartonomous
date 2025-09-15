# **Hartonomous Project: Master Development Steps (Zero-Context Edition v3)**

This document contains the complete, sequential, and unambiguous set of steps to build the Hartonomous prototype. Each step is a discrete, self-contained unit of work designed to be executed by a code-generation LLM like Claude.  
**Crucially, you must first read and permanently adhere to the "Guiding Principles" and "Project Standards" sections below. These govern *how* you approach every subsequent step in this document.**

## **Guiding Principles for AI Code Generation**

**Objective:** To provide a meta-level instruction set on *how* to approach each development step. You are to act as a senior software architect and developer. Your primary goal is not just to generate code, but to build a robust, secure, and maintainable production-quality system. You must follow these principles for every step.

### **1\. The Core Problem-Solving Loop (Reflexion & Tree of Thought)**

For every instruction, you must follow this mental process:

1. **Deconstruct:** Break the task into smaller, logical sub-problems.  
2. **Explore Options (Tree of Thought):** For any implementation choice, briefly consider at least two valid alternatives in comments.  
3. **State Intent (BDI Model):** State your chosen approach in a comment and justify it based on the project's core principles.  
4. **Implement:** Write clean, well-commented code.  
5. **Verify & Refine (Reflexion):** Perform a self-critique. Ask if the code satisfies all requirements, adheres to conventions, handles errors, is secure, and could be simplified. Correct any flaws.

### **2\. Foundational Technical Principles**

* **Security is Non-Negotiable:** Scope all data access by the User ID (oid claim) from the JWT token. Use parameterized queries. Treat all input as untrustworthy.  
* **Adhere to SOLID Principles** and **DRY (Don't Repeat Yourself)**.  
* **No Placeholders or Magic Values:** All code must be complete and functional for the given step.  
* **Robust Error Handling** and **Completeness** are mandatory.

## **Project Standards**

**Objective:** To establish global, unambiguous standards that will be strictly followed.

* **Root Namespace & Solution Name:** Hartonomous  
* **Project Naming:** Hartonomous.\<Layer\>.\<Feature/Concern\> (e.g., Hartonomous.Core, Hartonomous.Api.Ingestion, Hartonomous.Database).  
* **Database Objects:** PascalCase (e.g., dbo.Projects).

## **Module 1: Project Initialization**

**Objective:** To prepare the development environment and scaffold the project structure from an empty folder.

### **Step 1.1: Setup Initial Repository and Folder Structure**

* **Action:** Initialize a Git repository, create a standard .gitignore file, and scaffold the core directory structure.  
* **Instruction:** "You are starting in an empty directory. Generate and execute the necessary shell commands to perform the following setup steps in order:  
  1. Initialize a new Git repository.  
  2. Create a .gitignore file with standard ignore patterns for a .NET and Node.js (React) project. Include patterns for bin/, obj/, node\_modules/, .vs/, .user, .env, and common OS files.  
  3. Create the root solution directory named Hartonomous. All subsequent work will be inside this directory.  
  4. Inside the Hartonomous directory, create a src directory.  
  5. Inside the src directory, create subdirectories for each primary component: Api, Core, Database, Infrastructure, and UI."

## **Module 2: The Core Database (NinaDB)**

**Objective:** To establish the foundational data layer with proper multi-tenancy.

### **Step 2.1: Generate Database Schema Script**

* **Action:** Create an idempotent T-SQL script to provision the HartonomousDB schema.  
* **Instruction:** "Inside the Hartonomous directory, create a new directory named database. In this directory, create a file named schema.sql. The file must contain the complete, idempotent T-SQL script to create the HartonomousDB schema as defined in the provided code block. Do NOT create a dbo.Users table."  
  \-- This script assumes FILESTREAM is enabled at the SQL Server instance level.  
  IF NOT EXISTS (SELECT \* FROM sys.databases WHERE name \= 'HartonomousDB') CREATE DATABASE HartonomousDB;  
  GO  
  ALTER DATABASE HartonomousDB SET RECOVERY FULL;  
  GO  
  \-- Add FILESTREAM Filegroup if it doesn't exist  
  IF NOT EXISTS (SELECT \* FROM sys.filegroups WHERE groupname \= 'HartonomousFileStreamGroup')  
      ALTER DATABASE HartonomousDB ADD FILEGROUP HartonomousFileStreamGroup CONTAINS FILESTREAM;  
  GO  
  \-- Add FILESTREAM File if it doesn't exist  
  IF NOT EXISTS (SELECT \* FROM HartonomousDB.sys.database\_files WHERE name \= 'HartonomousModelWeightsFS')  
      ALTER DATABASE HartonomousDB ADD FILE (NAME \= 'HartonomousModelWeightsFS', FILENAME \= 'D:\\SQLData\\ModelWeightsFS') TO FILEGROUP HartonomousFileStreamGroup;  
  GO  
  USE HartonomousDB;  
  GO  
  \-- Define Tables  
  CREATE TABLE dbo.Projects ( ProjectId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), UserId NVARCHAR(128) NOT NULL, ProjectName NVARCHAR(256) NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE() );  
  CREATE TABLE dbo.ModelMetadata ( ModelId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), ProjectId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Projects(ProjectId), ModelName NVARCHAR(256) NOT NULL, Version NVARCHAR(50) NOT NULL, License NVARCHAR(100) NOT NULL, MetadataJson NVARCHAR(MAX), CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE() );  
  CREATE TABLE dbo.ModelComponents ( ComponentId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), ModelId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelMetadata(ModelId), ComponentName NVARCHAR(512) NOT NULL, ComponentType NVARCHAR(128) NOT NULL ) AS NODE WITH (SYSTEM\_VERSIONING \= ON (HISTORY\_TABLE \= dbo.ModelComponentsHistory));  
  CREATE TABLE dbo.ModelStructure ( CONSTRAINT EC\_ModelStructure CONNECTION ($from\_id TO $to\_id) ) AS EDGE;  
  CREATE TABLE dbo.ComponentWeights ( WeightId UNIQUEIDENTIFIER ROWGUIDCOL PRIMARY KEY DEFAULT NEWID(), ComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId) ON DELETE CASCADE, WeightData VARBINARY(MAX) FILESTREAM NULL );  
  CREATE TABLE dbo.ComponentEmbeddings ( EmbeddingId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), ComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId) ON DELETE CASCADE, EmbeddingVector VARCHAR(8000) NOT NULL );  
  CREATE TABLE dbo.OutboxEvents ( EventId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), EventType NVARCHAR(255) NOT NULL, Payload NVARCHAR(MAX) NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(), ProcessedAt DATETIME2 NULL );  
  GO  
  \-- Create JSON Index  
  IF NOT EXISTS (SELECT \* FROM sys.indexes WHERE name \= 'IX\_ModelMetadata\_JsonValue')  
  BEGIN  
      ALTER TABLE dbo.ModelMetadata ADD MetadataAuthor AS JSON\_VALUE(MetadataJson, '$.author') PERSISTED;  
      CREATE INDEX IX\_ModelMetadata\_JsonValue ON dbo.ModelMetadata(MetadataAuthor);  
  END  
  GO

### **Step 2.2: Generate SQL CLR C\# Code**

* **Action:** Create the C\# code for the SQL CLR assembly (Hartonomous.Database).  
* **Instruction:** "In the src/Database directory, create a new C\# Class Library project targeting .NET Framework 4.8. Name the project Hartonomous.Database. Replace the default class file with a file named FileStreamManager.cs containing the complete C\# code as defined below."  
  using System;  
  using System.Data.SqlTypes;  
  using Microsoft.SqlServer.Server;  
  using System.Data.SqlClient;

  public static class FileStreamManager  
  {  
      \[SqlProcedure\]  
      public static void SaveComponentWeight(SqlGuid componentId, SqlBytes data)  
      {  
          if (componentId.IsNull || data.IsNull) return;  
          using (SqlConnection conn \= new SqlConnection("context connection=true"))  
          {  
              conn.Open();  
              SqlTransaction tx \= conn.BeginTransaction();  
              try  
              {  
                  string getPathQuery \= "SELECT WeightData.PathName() FROM dbo.ComponentWeights WHERE ComponentId \= @ComponentId";  
                  string txContextQuery \= "SELECT GET\_FILESTREAM\_TRANSACTION\_CONTEXT()";  
                  byte\[\] txContext \= (byte\[\])new SqlCommand(txContextQuery, conn, tx).ExecuteScalar();  
                  string serverPath;  
                  using (var pathCmd \= new SqlCommand(getPathQuery, conn, tx))  
                  {  
                      pathCmd.Parameters.AddWithValue("@ComponentId", componentId.Value);  
                      serverPath \= (string)pathCmd.ExecuteScalar();  
                  }  
                  using (SqlFileStream sqlFileStream \= new SqlFileStream(serverPath, txContext, System.IO.FileAccess.Write))  
                  {  
                      sqlFileStream.Write(data.Value, 0, (int)data.Length);  
                  }  
                  tx.Commit();  
              }  
              catch  
              {  
                  tx.Rollback();  
                  throw;  
              }  
          }  
      }  
  }

### **Step 2.3: Generate SQL CLR Deployment Script**

* **Action:** Create the T-SQL script to deploy the CLR assembly from Step 2.2.  
* **Instruction:** "In the database directory, create a new file named deploy\_clr.sql. This file must contain the idempotent T-SQL script to deploy the compiled Hartonomous.Database.dll assembly to the HartonomousDB."  
  USE HartonomousDB;  
  GO  
  EXEC sp\_configure 'clr enabled', 1; RECONFIGURE;  
  GO  
  ALTER DATABASE HartonomousDB SET TRUSTWORTHY ON;  
  GO  
  IF OBJECT\_ID('dbo.usp\_SaveComponentWeight') IS NOT NULL DROP PROCEDURE dbo.usp\_SaveComponentWeight;  
  IF EXISTS (SELECT \* FROM sys.assemblies WHERE name \= 'HartonomousDatabaseAssembly') DROP ASSEMBLY HartonomousDatabaseAssembly;  
  GO  
  CREATE ASSEMBLY HartonomousDatabaseAssembly FROM 'C:\\Path\\To\\Your\\Project\\src\\Database\\Hartonomous.Database\\bin\\Debug\\Hartonomous.Database.dll' WITH PERMISSION\_SET \= EXTERNAL\_ACCESS;  
  GO  
  CREATE PROCEDURE dbo.usp\_SaveComponentWeight @componentId UNIQUEIDENTIFIER, @data VARBINARY(MAX) AS EXTERNAL NAME HartonomousDatabaseAssembly.\[FileStreamManager\].SaveComponentWeight;  
  GO

## **Module 3: Shared Backend Foundation**

**Objective:** To create reusable libraries for all backend services.

### **Step 3.1: Create C\# Solution & Core DTOs**

* **Action:** Create the C\# solution file and the Hartonomous.Core project with all DTOs.  
* **Instruction:** "In the root Hartonomous directory, create a new solution file named Hartonomous.sln. In the src/Core directory, create a new .NET 8 class library project named Hartonomous.Core. Add this project to the solution. In the Hartonomous.Core project, create a subfolder DTOs and add the following C\# record definitions in separate files."  
  // In Hartonomous.Core/DTOs/ProjectDtos.cs  
  namespace Hartonomous.Core.DTOs;  
  public record ProjectDto(Guid ProjectId, string ProjectName, DateTime CreatedAt);  
  public record CreateProjectRequest(string ProjectName);

  // In Hartonomous.Core/DTOs/ModelDtos.cs  
  namespace Hartonomous.Core.DTOs;  
  public record ModelMetadataDto(Guid ModelId, string ModelName, string Version, string License);

  // In Hartonomous.Core/DTOs/QueryDtos.cs  
  namespace Hartonomous.Core.DTOs;  
  public record QueryRequest(string SemanticQuery, Guid ProjectId);  
  public record ComponentDto(Guid ComponentId, string ComponentName, string ComponentType);  
  public record QueryResponse(List\<ComponentDto\> Components);

  // In Hartonomous.Core/DTOs/OrchestrationDtos.cs  
  namespace Hartonomous.Core.DTOs;  
  public record OrchestrationRequest(string Goal, Guid ProjectId);  
  public record OrchestrationResponse(string Status, Guid CorrelationId, List\<string\> Steps);

### **Step 3.2: Implement Shared Security Library**

* **Action:** Create and implement the Hartonomous.Infrastructure.Security project.  
* **Instruction:** "In the src/Infrastructure directory, create a new .NET 8 class library project named Hartonomous.Infrastructure.Security. Add it to the solution. Then, generate the complete C\# code for the SecurityServiceExtensions class. This requires the Microsoft.AspNetCore.Authentication.JwtBearer and Microsoft.Identity.Web NuGet packages."  
  using Microsoft.AspNetCore.Authentication.JwtBearer;  
  using Microsoft.Extensions.Configuration;  
  using Microsoft.Extensions.DependencyInjection;  
  using Microsoft.Identity.Web;  
  using System.Security.Claims;

  namespace Hartonomous.Infrastructure.Security;

  public static class SecurityServiceExtensions  
  {  
      public static IServiceCollection AddHartonomousAuthentication(this IServiceCollection services, IConfiguration config)  
      {  
          services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)  
              .AddMicrosoftIdentityWebApi(config.GetSection("AzureAd"));  
          return services;  
      }

      public static string GetUserId(this ClaimsPrincipal principal)  
      {  
          return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??   
                 throw new UnauthorizedAccessException("User ID claim (oid) not found in token.");  
      }  
  }

### **Step 3.3: Implement Shared Observability Library**

* **Action:** Create and implement the Hartonomous.Infrastructure.Observability project.  
* **Instruction:** "In the src/Infrastructure directory, create a new .NET 8 class library project named Hartonomous.Infrastructure.Observability. Add it to the solution. Then, generate the complete C\# code for the ObservabilityServiceExtensions class. This requires NuGet packages: OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.Http, OpenTelemetry.Instrumentation.SqlClient, and OpenTelemetry.Exporter.Console."  
  using Microsoft.Extensions.DependencyInjection;  
  using OpenTelemetry.Metrics;  
  using OpenTelemetry.Trace;  
  using OpenTelemetry.Resources;

  namespace Hartonomous.Infrastructure.Observability;

  public static class ObservabilityServiceExtensions  
  {  
      public static IServiceCollection AddHartonomousObservability(this IServiceCollection services, string serviceName)  
      {  
          var resourceBuilder \= ResourceBuilder.CreateDefault().AddService(serviceName);

          services.AddOpenTelemetry()  
              .WithMetrics(metrics \=\> metrics  
                  .SetResourceBuilder(resourceBuilder)  
                  .AddAspNetCoreInstrumentation()  
                  .AddConsoleExporter())  
              .WithTracing(tracing \=\> tracing  
                  .SetResourceBuilder(resourceBuilder)  
                  .AddAspNetCoreInstrumentation()  
                  .AddHttpClientInstrumentation()  
                  .AddSqlClientInstrumentation()  
                  .AddConsoleExporter());

          return services;  
      }  
  }  
