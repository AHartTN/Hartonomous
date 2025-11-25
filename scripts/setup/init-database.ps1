#Requires -Version 5.1
<#
.SYNOPSIS
    Hartonomous Database Initialization Script (Windows)
    
.DESCRIPTION
    Initializes the Hartonomous database by:
    - Loading required extensions
    - Creating custom types
    - Creating tables
    - Creating indexes
    - Creating triggers
    - Creating functions
    - Creating views
    
.NOTES
    Author: Anthony Hart
    Copyright (c) 2025 Anthony Hart. All Rights Reserved.
#>

[CmdletBinding()]
param(
    [string]$PgHost = "localhost",
    [string]$PgPort = "5432",
    [string]$PgUser = "postgres",
    [string]$PgDatabase = "hartonomous",
    [string]$SchemaPath = "../../schema"
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-ColorOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-ColorOutput "============================================" "Cyan"
    Write-ColorOutput $Title "Cyan"
    Write-ColorOutput "============================================" "Cyan"
}

function Execute-SqlFile {
    param([string]$FilePath, [string]$Description)
    
    if (-not (Test-Path $FilePath)) {
        Write-ColorOutput "  WARNING: File not found: $FilePath" "Yellow"
        return
    }
    
    Write-ColorOutput "  Executing: $Description" "Gray"
    
    $env:PGPASSWORD = $env:POSTGRES_PASSWORD
    psql -h $PgHost -p $PgPort -U $PgUser -d $PgDatabase -f $FilePath -v ON_ERROR_STOP=1 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput "  ERROR: Failed to execute $FilePath" "Red"
        throw "SQL execution failed"
    }
}

function Execute-SqlDirectory {
    param([string]$DirectoryPath, [string]$Description)
    
    if (-not (Test-Path $DirectoryPath)) {
        Write-ColorOutput "  WARNING: Directory not found: $DirectoryPath" "Yellow"
        return
    }
    
    Write-Section $Description
    
    Get-ChildItem -Path $DirectoryPath -Filter "*.sql" | Sort-Object Name | ForEach-Object {
        Execute-SqlFile -FilePath $_.FullName -Description $_.Name
    }
    
    Write-ColorOutput "  ? Complete" "Green"
}

# Main execution
try {
    Write-Section "Hartonomous Database Initialization"
    Write-ColorOutput "Host: $PgHost" "Gray"
    Write-ColorOutput "Port: $PgPort" "Gray"
    Write-ColorOutput "Database: $PgDatabase" "Gray"
    Write-ColorOutput ""
    
    # Extensions (AGE must be loaded first for provenance)
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/extensions" -Description "Extensions"
    
    # Custom Types
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/types" -Description "Custom Types"
    
    # Tables
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/tables" -Description "Tables"
    
    # Indexes (atomized by category)
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/indexes/spatial" -Description "Spatial Indexes"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/indexes/core" -Description "Core Indexes"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/indexes/composition" -Description "Composition Indexes"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/indexes/relations" -Description "Relation Indexes"
    
    # Triggers (includes LISTEN/NOTIFY for AGE sync)
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/triggers" -Description "Triggers"
    
    # AGE Provenance Graph Schema (CQRS Query Side)
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/age" -Description "AGE Provenance Graph"
    
    # Functions (atomized by domain) - HELPERS FIRST
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/helpers" -Description "Helper Functions"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/atomization" -Description "Atomization Functions"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/spatial" -Description "Spatial Functions"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/composition" -Description "Composition Functions"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/relations" -Description "Relation Functions"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/provenance" -Description "Provenance Functions"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/inference" -Description "AI Inference Functions"
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/core/functions/ooda" -Description "OODA Functions"
    
    # Views
    Execute-SqlDirectory -DirectoryPath "$SchemaPath/views" -Description "Views"
    
    Write-Section "Initialization Complete"
    Write-ColorOutput "? Hartonomous database is ready!" "Green"
    Write-ColorOutput ""
    Write-ColorOutput "Quick start:" "Cyan"
    Write-ColorOutput "  psql -h $PgHost -U $PgUser -d $PgDatabase" "Gray"
    Write-ColorOutput "  SELECT atomize_text('Hello Hartonomous');" "Gray"
    
} catch {
    Write-ColorOutput ""
    Write-ColorOutput "? Initialization failed: $_" "Red"
    exit 1
}
