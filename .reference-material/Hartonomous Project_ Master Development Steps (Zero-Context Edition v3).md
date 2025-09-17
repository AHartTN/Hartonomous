Hartonomous Project: Master Development Steps (Zero-Context Edition v3)This document contains the complete, sequential, and unambiguous set of steps to build the Hartonomous prototype. Each step is a discrete, self-contained unit of work designed to be executed by a code-generation LLM like Claude.Crucially, you must first read and permanently adhere to the "Guiding Principles" and "Project Standards" sections below. These govern how you approach every subsequent step in this document.Guiding Principles for AI Code GenerationObjective: To provide a meta-level instruction set on how to approach each development step. You are to act as a senior software architect and developer. Your primary goal is not just to generate code, but to build a robust, secure, and maintainable production-quality system. You must follow these principles for every step.1. The Core Problem-Solving Loop (Reflexion & Tree of Thought)For every instruction, you must follow this mental process:Deconstruct: Break the task into smaller, logical sub-problems.Explore Options (Tree of Thought): For any implementation choice, briefly consider at least two valid alternatives in comments.State Intent (BDI Model): State your chosen approach in a comment and justify it based on the project's core principles.Implement: Write clean, well-commented code.Verify & Refine (Reflexion): Perform a self-critique. Ask if the code satisfies all requirements, adheres to conventions, handles errors, is secure, and could be simplified. Correct any flaws.2. Foundational Technical PrinciplesSecurity is Non-Negotiable: Scope all data access by the User ID (oid claim) from the JWT token. Use parameterized queries. Treat all input as untrustworthy.Adhere to SOLID Principles and DRY (Don't Repeat Yourself).No Placeholders or Magic Values: All code must be complete and functional for the given step.Robust Error Handling and Completeness are mandatory.Project StandardsObjective: To establish global, unambiguous standards that will be strictly followed.Root Namespace & Solution Name: HartonomousProject Naming: Hartonomous.<Layer>.<Feature/Concern> (e.g., Hartonomous.Core, Hartonomous.Api.Ingestion, Hartonomous.Database).Database Objects: PascalCase (e.g., dbo.Projects).Module 1: Project InitializationObjective: To prepare the development environment and scaffold the project structure from an empty folder.Step 1.1: Setup Initial Repository and Folder StructureAction: Initialize a Git repository, create a standard .gitignore file, and scaffold the core directory structure.Instruction: "You are starting in an empty directory. Generate and execute the necessary shell commands to perform the following setup steps in order:Initialize a new Git repository.Create a .gitignore file with standard ignore patterns for a .NET and Node.js (React) project. Include patterns for bin/, obj/, node_modules/, .vs/, .user, .env, and common OS files.Create the root solution directory named Hartonomous. All subsequent work will be inside this directory.Inside the Hartonomous directory, create a src directory.Inside the src directory, create subdirectories for each primary component: Api, Core, Database, Infrastructure, and UI."Module 2: The Core Database (NinaDB)Objective: To establish the foundational data layer with proper multi-tenancy.Step 2.1: Generate Database Schema ScriptAction: Create an idempotent T-SQL script to provision the HartonomousDB schema.Instruction: "Inside the Hartonomous directory, create a new directory named database. In this directory, create a file named schema.sql. The file must contain the complete, idempotent T-SQL script to create the HartonomousDB schema as defined in the provided code block. Do NOT create a dbo.Users table."-- This script assumes FILESTREAM is enabled at the SQL Server instance level.
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'HartonomousDB') CREATE DATABASE HartonomousDB;
GO
ALTER DATABASE HartonomousDB SET RECOVERY FULL;
GO
IF NOT EXISTS (SELECT * FROM sys.filegroups WHERE groupname = 'HartonomousFileStreamGroup')
    ALTER DATABASE HartonomousDB ADD FILEGROUP HartonomousFileStreamGroup CONTAINS FILESTREAM;
GO
IF NOT EXISTS (SELECT * FROM HartonomousDB.sys.database_files WHERE name = 'HartonomousModelWeightsFS')
    ALTER DATABASE HartonomousDB ADD FILE (NAME = 'HartonomousModelWeightsFS', FILENAME = 'D:\SQLData\ModelWeightsFS') TO FILEGROUP HartonomousFileStreamGroup;
GO
USE HartonomousDB;
GO
CREATE TABLE dbo.Projects ( ProjectId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), UserId NVARCHAR(128) NOT NULL, ProjectName NVARCHAR(256) NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE() );
CREATE TABLE dbo.ModelMetadata ( ModelId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), ProjectId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Projects(ProjectId), ModelName NVARCHAR(256) NOT NULL, Version NVARCHAR(50) NOT NULL, License NVARCHAR(100) NOT NULL, MetadataJson NVARCHAR(MAX), CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE() );
CREATE TABLE dbo.ModelComponents ( ComponentId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), ModelId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelMetadata(ModelId), ComponentName NVARCHAR(512) NOT NULL, ComponentType NVARCHAR(128) NOT NULL ) AS NODE WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.ModelComponentsHistory));
CREATE TABLE dbo.ModelStructure ( CONSTRAINT EC_ModelStructure CONNECTION ($from_id TO $to_id) ) AS EDGE;
CREATE TABLE dbo.ComponentWeights ( WeightId UNIQUEIDENTIFIER ROWGUIDCOL PRIMARY KEY DEFAULT NEWID(), ComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId) ON DELETE CASCADE, WeightData VARBINARY(MAX) FILESTREAM NULL );
CREATE TABLE dbo.ComponentEmbeddings ( EmbeddingId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), ComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId) ON DELETE CASCADE, EmbeddingVector VARCHAR(8000) NOT NULL );
CREATE TABLE dbo.OutboxEvents ( EventId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(), EventType NVARCHAR(255) NOT NULL, Payload NVARCHAR(MAX) NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(), ProcessedAt DATETIME2 NULL );
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ModelMetadata_JsonValue')
BEGIN
    ALTER TABLE dbo.ModelMetadata ADD MetadataAuthor AS JSON_VALUE(MetadataJson, '$.author') PERSISTED;
    CREATE INDEX IX_ModelMetadata_JsonValue ON dbo.ModelMetadata(MetadataAuthor);
END
GO
Step 2.2: Generate SQL CLR C# CodeAction: Create the C# code for the SQL CLR assembly (Hartonomous.Database).Instruction: "In the src/Database directory, create a new C# Class Library project targeting .NET Framework 4.8. Name the project Hartonomous.Database. Replace the default class file with a file named FileStreamManager.cs containing the complete C# code as defined below."using System;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using System.Data.SqlClient;

public static class FileStreamManager
{
    [SqlProcedure]
    public static void SaveComponentWeight(SqlGuid componentId, SqlBytes data)
    {
        if (componentId.IsNull || data.IsNull) return;
        using (SqlConnection conn = new SqlConnection("context connection=true"))
        {
            conn.Open();
            SqlTransaction tx = conn.BeginTransaction();
            try
            {
                string getPathQuery = "SELECT WeightData.PathName() FROM dbo.ComponentWeights WHERE ComponentId = @ComponentId";
                string txContextQuery = "SELECT GET_FILESTREAM_TRANSACTION_CONTEXT()";
                byte[] txContext = (byte[])new SqlCommand(txContextQuery, conn, tx).ExecuteScalar();
                string serverPath;
                using (var pathCmd = new SqlCommand(getPathQuery, conn, tx))
                {
                    pathCmd.Parameters.AddWithValue("@ComponentId", componentId.Value);
                    serverPath = (string)pathCmd.ExecuteScalar();
                }
                using (SqlFileStream sqlFileStream = new SqlFileStream(serverPath, txContext, System.IO.FileAccess.Write))
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
Step 2.3: Generate SQL CLR Deployment ScriptAction: Create the T-SQL script to deploy the CLR assembly from Step 2.2.Instruction: "In the database directory, create a new file named deploy_clr.sql. This file must contain the idempotent T-SQL script to deploy the compiled Hartonomous.Database.dll assembly to the HartonomousDB."USE HartonomousDB;
GO
EXEC sp_configure 'clr enabled', 1; RECONFIGURE;
GO
ALTER DATABASE HartonomousDB SET TRUSTWORTHY ON;
GO
IF OBJECT_ID('dbo.usp_SaveComponentWeight') IS NOT NULL DROP PROCEDURE dbo.usp_SaveComponentWeight;
IF EXISTS (SELECT * FROM sys.assemblies WHERE name = 'HartonomousDatabaseAssembly') DROP ASSEMBLY HartonomousDatabaseAssembly;
GO
CREATE ASSEMBLY HartonomousDatabaseAssembly FROM 'C:\Path\To\Your\Project\src\Database\Hartonomous.Database\bin\Debug\Hartonomous.Database.dll' WITH PERMISSION_SET = EXTERNAL_ACCESS;
GO
CREATE PROCEDURE dbo.usp_SaveComponentWeight @componentId UNIQUEIDENTIFIER, @data VARBINARY(MAX) AS EXTERNAL NAME HartonomousDatabaseAssembly.[FileStreamManager].SaveComponentWeight;
GO
Module 3: Shared Backend FoundationObjective: To create reusable libraries for all backend services.Step 3.1: Create C# Solution & Core DTOsAction: Create the C# solution file and the Hartonomous.Core project with all DTOs.Instruction: "In the root Hartonomous directory, create a new solution file named Hartonomous.sln. In the src/Core directory, create a new .NET 8 class library project named Hartonomous.Core. Add this project to the solution. In the Hartonomous.Core project, create a subfolder DTOs and add the following C# record definitions in separate files."// In Hartonomous.Core/DTOs/ProjectDtos.cs
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
public record QueryResponse(List<ComponentDto> Components);

// In Hartonomous.Core/DTOs/OrchestrationDtos.cs
namespace Hartonomous.Core.DTOs;
public record OrchestrationRequest(string Goal, Guid ProjectId);
public record OrchestrationResponse(string Status, Guid CorrelationId, List<string> Steps);
Step 3.2: Implement Shared Security LibraryAction: Create and implement the Hartonomous.Infrastructure.Security project.Instruction: "In the src/Infrastructure directory, create a new .NET 8 class library project named Hartonomous.Infrastructure.Security. Add it to the solution. Then, generate the complete C# code for the SecurityServiceExtensions class. This requires the Microsoft.AspNetCore.Authentication.JwtBearer and Microsoft.Identity.Web NuGet packages."using Microsoft.AspNetCore.Authentication.JwtBearer;
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
Step 3.3: Implement Shared Observability LibraryAction: Create and implement the Hartonomous.Infrastructure.Observability project.Instruction: "In the src/Infrastructure directory, create a new .NET 8 class library project named Hartonomous.Infrastructure.Observability. Add it to the solution. Then, generate the complete C# code for the ObservabilityServiceExtensions class. This requires NuGet packages: OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.Http, OpenTelemetry.Instrumentation.SqlClient, and OpenTelemetry.Exporter.Console."using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

namespace Hartonomous.Infrastructure.Observability;

public static class ObservabilityServiceExtensions
{
    public static IServiceCollection AddHartonomousObservability(this IServiceCollection services, string serviceName)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName);

        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddConsoleExporter())
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddConsoleExporter());

        return services;
    }
}
Module 4: The API ServicesObjective: To build the three distinct backend microservices.Step 4.1: Create & Implement Ingestion APIAction: Create the Hartonomous.Api.Ingestion service with its project creation endpoint.Instruction: "In the src/Api directory, create a new ASP.NET Core 8 Web API project named Hartonomous.Api.Ingestion. Add it to the solution and reference the necessary shared libraries. Replace the Program.cs file with the complete code below. This requires the Dapper NuGet package."using Hartonomous.Core.DTOs;
using Hartonomous.Infrastructure.Security;
using Hartonomous.Infrastructure.Observability;
using Microsoft.Data.SqlClient;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHartonomousAuthentication(builder.Configuration);
builder.Services.AddHartonomousObservability("Hartonomous.Api.Ingestion");
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/v1/projects", async (CreateProjectRequest req, HttpContext httpContext, IConfiguration config) =>
{
    var userId = httpContext.User.GetUserId();
    var newProjectId = Guid.NewGuid();
    var sql = "INSERT INTO dbo.Projects (ProjectId, UserId, ProjectName) VALUES (@ProjectId, @UserId, @ProjectName)";

    await using var connection = new SqlConnection(config.GetConnectionString("HartonomousDB"));
    await connection.ExecuteAsync(sql, new { ProjectId = newProjectId, UserId = userId, req.ProjectName });

    return Results.Created($"/api/v1/projects/{newProjectId}", new { ProjectId = newProjectId });
}).RequireAuthorization();

app.Run();
Step 4.2: Create & Implement Query APIAction: Create the Hartonomous.Api.Query service with its component query endpoint.Instruction: "In the src/Api directory, create a new ASP.NET Core 8 Web API project named Hartonomous.Api.Query. Add it to the solution and reference shared libraries. Replace the Program.cs file with the complete code below. This requires the Dapper NuGet package."using Hartonomous.Core.DTOs;
using Hartonomous.Infrastructure.Security;
using Hartonomous.Infrastructure.Observability;
using Microsoft.Data.SqlClient;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHartonomousAuthentication(builder.Configuration);
builder.Services.AddHartonomousObservability("Hartonomous.Api.Query");
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/v1/query/components", async (QueryRequest req, HttpContext httpContext, IConfiguration config) =>
{
    var userId = httpContext.User.GetUserId();
    // NOTE: This is a simplified query. A real implementation would convert
    // req.SemanticQuery into a vector and use vector search.
    var sql = @"
        SELECT c.ComponentId, c.ComponentName, c.ComponentType
        FROM dbo.ModelComponents AS c
        INNER JOIN dbo.ModelMetadata AS m ON c.ModelId = m.ModelId
        INNER JOIN dbo.Projects AS p ON m.ProjectId = p.ProjectId
        WHERE p.UserId = @UserId AND p.ProjectId = @ProjectId
        AND c.ComponentName LIKE '%' + @SearchTerm + '%';";

    await using var connection = new SqlConnection(config.GetConnectionString("HartonomousDB"));
    var components = await connection.QueryAsync<ComponentDto>(sql, new { UserId = userId, req.ProjectId, SearchTerm = req.SemanticQuery });

    return Results.Ok(new QueryResponse(components.AsList()));
}).RequireAuthorization();

app.Run();
Step 4.3: Create & Implement Orchestration APIAction: Create the Hartonomous.Api.Orchestration service with its orchestration endpoint.Instruction: "In the src/Api directory, create a new ASP.NET Core 8 Web API project named Hartonomous.Api.Orchestration. Add it to the solution and reference shared libraries. Replace Program.cs with the complete code below."using Hartonomous.Core.DTOs;
using Hartonomous.Infrastructure.Security;
using Hartonomous.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHartonomousAuthentication(builder.Configuration);
builder.Services.AddHartonomousObservability("Hartonomous.Api.Orchestration");
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // Add IHttpClientFactory

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/v1/orchestrate", async (OrchestrationRequest req, HttpContext httpContext, IHttpClientFactory clientFactory) =>
{
    var userId = httpContext.User.GetUserId();
    // This is a placeholder for a complex orchestration logic (e.g., LangGraph).
    // It demonstrates making an authenticated, on-behalf-of call to another service.
    var steps = new List<string>
    {
        "Step 1: Goal received - " + req.Goal,
        "Step 2: User identified - " + userId,
        "Step 3: (Simulated) Called Query API to find relevant components.",
        "Step 4: (Simulated) Dispatched work to agents.",
        "Step 5: Orchestration complete."
    };

    var response = new OrchestrationResponse("Completed", Guid.NewGuid(), steps);
    return Results.Ok(response);
}).RequireAuthorization();

app.Run();
Module 5: The Frontend ApplicationObjective: To build the user-facing web application.Step 5.1: Create React App with Auth, API Hook, and UIAction: Create the complete single-file React application and its entry point.Instruction: "In the src/UI directory, initialize a new React project using Vite (npm create vite@latest . -- --template react). Then, install the required dependencies: npm install @azure/msal-browser @azure/msal-react tailwindcss postcss autoprefixer && npx tailwindcss init -p. Replace the contents of App.jsx and main.jsx with the code below."// in src/UI/main.jsx
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App.jsx';
import './index.css'; // Assumes TailwindCSS setup
import { PublicClientApplication } from "@azure/msal-browser";
import { MsalProvider } from "@azure/msal-react";

const msalConfig = {
  auth: {
    clientId: "YOUR_FRONTEND_APP_CLIENT_ID_HERE",
    authority: "https://<YOUR_TENANT_NAME>[.ciamlogin.com/](https://.ciamlogin.com/)",
    redirectUri: "http://localhost:5173"
  }
};
const msalInstance = new PublicClientApplication(msalConfig);

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <MsalProvider instance={msalInstance}>
      <App />
    </MsalProvider>
  </React.StrictMode>,
);

// in src/UI/App.jsx
import React, { useState } from 'react';
import { useMsal, useIsAuthenticated, AuthenticatedTemplate, UnauthenticatedTemplate } from "@azure/msal-react";
import { InteractionStatus, InteractionRequiredAuthError } from "@azure/msal-browser";

const useAuthenticatedApi = () => {
    const { instance, inProgress } = useMsal();
    const apiScopes = ["api://YOUR_BACKEND_API_CLIENT_ID_HERE/access_as_user"];

    const getApiClient = async () => {
        if (inProgress === InteractionStatus.None) {
            const accounts = instance.getAllAccounts();
            if (accounts.length > 0) {
                try {
                    const response = await instance.acquireTokenSilent({ scopes: apiScopes, account: accounts[0] });
                    return (url, options) => {
                        const newOptions = { ...options };
                        newOptions.headers = { ...newOptions.headers, 'Authorization': `Bearer ${response.accessToken}`, 'Content-Type': 'application/json' };
                        return fetch(url, newOptions);
                    };
                } catch (error) {
                   if (error instanceof InteractionRequiredAuthError) {
                       instance.acquireTokenRedirect({ scopes: apiScopes });
                   }
                   console.error(error);
                }
            }
        }
    };
    return { getApiClient };
};

function App() {
  const isAuthenticated = useIsAuthenticated();
  const { instance } = useMsal();
  return (
      <div className="bg-gray-900 text-white min-h-screen p-8 font-sans">
          <header className="flex justify-between items-center mb-12">
              <h1 className="text-3xl font-bold">Hartonomous</h1>
              {isAuthenticated ? <button onClick={() => instance.logoutRedirect()} className="bg-red-500 px-4 py-2 rounded">Logout</button> : <button onClick={() => instance.loginRedirect()} className="bg-blue-500 px-4 py-2 rounded">Login</button>}
          </header>
          <main>
              <UnauthenticatedTemplate><p className="text-center text-lg">Please log in to manage your AI models.</p></UnauthenticatedTemplate>
              <AuthenticatedTemplate><ProjectManager /></AuthenticatedTemplate>
          </main>
      </div>
  );
}

function ProjectManager() {
    const { getApiClient } = useAuthenticatedApi();
    const [projectName, setProjectName] = useState("");

    const handleCreateProject = async (e) => {
        e.preventDefault();
        const apiClient = await getApiClient();
        if (apiClient) {
            const response = await apiClient("/api/v1/projects", { method: 'POST', body: JSON.stringify({ projectName }) });
            if(response.ok) { alert('Project created!'); setProjectName(""); } else { alert('Failed to create project.'); }
        }
    };

    return (
        <div className="max-w-md mx-auto bg-gray-800 p-6 rounded-lg">
            <h2 className="text-2xl mb-4">Create New Project</h2>
            <form onSubmit={handleCreateProject}>
                <input type="text" value={projectName} onChange={(e) => setProjectName(e.target.value)} placeholder="Enter project name" className="w-full bg-gray-700 p-2 rounded mb-4 text-white" />
                <button type="submit" className="w-full bg-green-600 p-2 rounded">Create Project</button>
            </form>
        </div>
    );
}

export default App;
Module 6: Infrastructure as CodeObjective: To define the entire cloud deployment using Terraform.Step 6.1: Generate Complete Terraform PlanAction: Create the complete Terraform plan for all Azure resources.Instruction: "In the Hartonomous directory, create a new directory terraform. Inside it, create two files: main.tf and variables.tf. The files must define all necessary resources for the solution, including the Resource Group, App Services, Azure SQL, and the Azure AD App Registrations with correctly configured API permissions."# in terraform/variables.tf
variable "resource_group_name" { type = string; default = "rg-hartonomous-prod" }
variable "location" { type = string; default = "East US" }
variable "sql_admin_login" { type = string; }
variable "sql_admin_password" { type = string; sensitive = true; }

# in terraform/main.tf
terraform {
  required_providers {
    azurerm = { source = "hashicorp/azurerm"; version = "~>3.0" }
    azuread = { source = "hashicorp/azuread"; version = "~>2.0" }
    random = { source = "hashicorp/random"; version = "~>3.0" }
  }
}
provider "azurerm" { features {} }
provider "azuread" {}

resource "random_uuid" "backend_api_id" {}
resource "random_uuid" "api_scope_id" {}

resource "azurerm_resource_group" "main" {
  name     = var.resource_group_name
  location = var.location
}

resource "azuread_application" "backend_api" {
  display_name    = "Hartonomous Backend API"
  identifier_uris = ["api://${random_uuid.backend_api_id.result}"]
  api {
    oauth2_permission_scope {
      admin_consent_description  = "Allow the application to access the Hartonomous API on behalf of the signed-in user."
      admin_consent_display_name = "Access Hartonomous API"
      enabled                    = true
      id                         = random_uuid.api_scope_id.result
      type                       = "User"
      value                      = "access_as_user"
    }
  }
}

resource "azuread_application" "frontend_spa" {
  display_name     = "Hartonomous Frontend SPA"
  sign_in_audience = "AzureADMyOrg"
  spa { redirect_uris = ["https://<YOUR_STATIC_SITE_HOSTNAME>"] }
  required_resource_access {
    resource_app_id = azuread_application.backend_api.application_id
    resource_access {
      id   = random_uuid.api_scope_id.result
      type = "Scope"
    }
  }
}

# NOTE: Additional resources for SQL Server, App Service Plans,
# App Services, Key Vault, and Static Web App would follow,
# referencing these App Registrations.
