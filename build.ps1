<#
.SYNOPSIS
    Hartonomous Build Script - One-command build, test, and deploy
.DESCRIPTION
    Builds native C++ library, copies to .NET projects, builds .NET, runs tests.
    Supports Debug/Release modes and optional seeding.
.PARAMETER Mode
    Build mode: Debug (default) or Release
.PARAMETER Clean
    Clean all build artifacts before building
.PARAMETER Test
    Run tests after building
.PARAMETER Seed
    Seed the database after building (requires PostgreSQL)
.PARAMETER SkipNative
    Skip native C++ build (use existing DLL)
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Mode Release -Test
    .\build.ps1 -Clean -Test -Seed
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Mode = "Debug",
    [switch]$Clean,
    [switch]$Test,
    [switch]$Seed,
    [switch]$SkipNative
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$NativeDir = Join-Path $Root "Hartonomous.Native"
$CLIDir = Join-Path $Root "Hartonomous.CLI"
$TerminalDir = Join-Path $Root "Hartonomous.Terminal"

# Determine preset based on mode
$Preset = if ($Mode -eq "Release") { "windows-clang-release" } else { "windows-clang-debug" }
$BuildDir = Join-Path $NativeDir "out\build\$Preset"
$DllSource = Join-Path $BuildDir "bin\libHartonomous.Native.dll"

function Write-Step($message) {
    Write-Host "`n=== $message ===" -ForegroundColor Cyan
}

function Write-Success($message) {
    Write-Host $message -ForegroundColor Green
}

function Write-Warning($message) {
    Write-Host $message -ForegroundColor Yellow
}

try {
    Write-Host "Hartonomous Build System" -ForegroundColor Magenta
    Write-Host "========================" -ForegroundColor Magenta
    Write-Host "Mode: $Mode"

    # =========================================================================
    # Clean
    # =========================================================================
    if ($Clean) {
        Write-Step "Cleaning build artifacts"
        
        # Clean native
        $OutDir = Join-Path $NativeDir "out"
        if (Test-Path $OutDir) {
            Remove-Item $OutDir -Recurse -Force
            Write-Host "Removed: $OutDir"
        }

        # Clean .NET
        dotnet clean "$Root\Hartonomous.slnx" --configuration $Mode 2>$null
        Write-Success "Clean complete"
    }

    # =========================================================================
    # Native Build
    # =========================================================================
    if (-not $SkipNative) {
        Write-Step "Building Native Library (C++)"
        
        Push-Location $NativeDir
        try {
            # Configure
            cmake --preset $Preset
            if ($LASTEXITCODE -ne 0) { throw "CMake configure failed" }

            # Build
            cmake --build $BuildDir --config $Mode --parallel
            if ($LASTEXITCODE -ne 0) { throw "CMake build failed" }
        }
        finally {
            Pop-Location
        }

        Write-Success "Native build complete: $DllSource"
    }

    # =========================================================================
    # Copy DLL to .NET Projects
    # =========================================================================
    Write-Step "Deploying Native DLL to .NET Projects"

    if (-not (Test-Path $DllSource)) {
        throw "Native DLL not found: $DllSource. Run without -SkipNative."
    }

    $DotNetConfig = $Mode
    $DotNetTargets = @(
        @{ Project = $CLIDir; RID = "win-x64" },
        @{ Project = $TerminalDir; RID = "win-x64" }
    )

    foreach ($target in $DotNetTargets) {
        $binDir = Join-Path $target.Project "bin\$DotNetConfig\net10.0\$($target.RID)"
        if (-not (Test-Path $binDir)) {
            New-Item -ItemType Directory -Path $binDir -Force | Out-Null
        }

        # Copy as both names (lib prefix for Linux compat, without for Windows)
        $destWithLib = Join-Path $binDir "libHartonomous.Native.dll"
        $destNoLib = Join-Path $binDir "Hartonomous.Native.dll"
        
        Copy-Item $DllSource $destWithLib -Force
        Copy-Item $DllSource $destNoLib -Force
        Write-Host "  -> $destNoLib"
    }

    Write-Success "DLL deployment complete"

    # =========================================================================
    # .NET Build
    # =========================================================================
    Write-Step "Building .NET Projects"

    # Build core projects (skip mobile apps that require Mono runtime)
    $CoreProjects = @(
        "Hartonomous.Core\Hartonomous.Core.csproj",
        "Hartonomous.Infrastructure\Hartonomous.Infrastructure.csproj",
        "Hartonomous.CLI\Hartonomous.CLI.csproj",
        "Hartonomous.Terminal\Hartonomous.Terminal.csproj",
        "Hartonomous.Worker\Hartonomous.Worker.csproj"
    )

    foreach ($proj in $CoreProjects) {
        $projPath = Join-Path $Root $proj
        if (Test-Path $projPath) {
            Write-Host "Building $proj..."
            dotnet build $projPath --configuration $DotNetConfig --no-restore 2>$null
            if ($LASTEXITCODE -ne 0) {
                # Try with restore
                dotnet build $projPath --configuration $DotNetConfig
                if ($LASTEXITCODE -ne 0) { throw "Failed to build $proj" }
            }
        }
    }

    Write-Success ".NET build complete"

    # =========================================================================
    # Tests
    # =========================================================================
    if ($Test) {
        Write-Step "Running Native Tests"
        
        $TestExe = Join-Path $BuildDir "bin\hartonomous-tests.exe"
        if (Test-Path $TestExe) {
            & $TestExe
            if ($LASTEXITCODE -ne 0) { throw "Native tests failed" }
            Write-Success "Native tests passed"
        } else {
            Write-Warning "Native test executable not found: $TestExe"
        }

        Write-Step "Running .NET Tests"
        dotnet test "$Root\Hartonomous.slnx" --configuration $DotNetConfig --no-build
        if ($LASTEXITCODE -ne 0) { throw ".NET tests failed" }
        Write-Success ".NET tests passed"
    }

    # =========================================================================
    # Database Seeding
    # =========================================================================
    if ($Seed) {
        Write-Step "Seeding Database"

        $SeedExe = Join-Path $BuildDir "bin\hartonomous-seed.exe"
        if (Test-Path $SeedExe) {
            $env:HARTONOMOUS_DB_URL = "postgresql://hartonomous:hartonomous@localhost/hartonomous"
            & $SeedExe
            if ($LASTEXITCODE -ne 0) { throw "Database seeding failed" }
            Write-Success "Database seeding complete"
        } else {
            Write-Warning "Seeder not built (requires PostgreSQL dev libraries)"
            Write-Host "Start PostgreSQL with: docker compose up -d postgres"
            Write-Host "Then seed manually or rebuild native with PostgreSQL support"
        }
    }

    # =========================================================================
    # Summary
    # =========================================================================
    Write-Host "`n" -NoNewline
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " BUILD SUCCESSFUL " -ForegroundColor Green -BackgroundColor DarkGreen
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Run CLI:      .\Hartonomous.CLI\bin\$DotNetConfig\net10.0\win-x64\hartonomous.exe"
    Write-Host "Run Terminal: .\Hartonomous.Terminal\bin\$DotNetConfig\net10.0\win-x64\hartonomous-terminal.exe"
    Write-Host ""

} catch {
    Write-Host "`n" -NoNewline
    Write-Host "========================================" -ForegroundColor Red
    Write-Host " BUILD FAILED " -ForegroundColor Red -BackgroundColor DarkRed
    Write-Host "========================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
