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

# Determine preset and RID based on OS and mode
$IsWindows = $IsWindows -or ($env:OS -eq "Windows_NT")
$IsMacOS = $IsMacOS -or ($PSVersionTable.Platform -eq "Unix" -and (uname) -eq "Darwin")
$IsLinux = $IsLinux -or ($PSVersionTable.Platform -eq "Unix" -and (uname) -eq "Linux")

if ($IsWindows) {
    $Preset = if ($Mode -eq "Release") { "windows-clang-release" } else { "windows-clang-debug" }
    $RID = "win-x64"
    $LibPrefix = ""
    $LibExt = ".dll"
} elseif ($IsMacOS) {
    $Preset = if ($Mode -eq "Release") { "macos-clang-release" } else { "macos-clang-debug" }
    $RID = "osx-x64"
    $LibPrefix = "lib"
    $LibExt = ".dylib"
} elseif ($IsLinux) {
    $Preset = if ($Mode -eq "Release") { "linux-clang-release" } else { "linux-clang-debug" }
    $RID = "linux-x64"
    $LibPrefix = "lib"
    $LibExt = ".so"
} else {
    throw "Unsupported platform"
}

$BuildDir = Join-Path $Root "artifacts/native/build/$Preset"
$LibName = "${LibPrefix}Hartonomous.Native${LibExt}"
$LibSource = Join-Path $BuildDir "bin/$LibName"

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
    # Copy Native Library to .NET Projects
    # =========================================================================
    Write-Step "Deploying Native Library to .NET Projects"

    if (-not (Test-Path $LibSource)) {
        throw "Native library not found: $LibSource. Run without -SkipNative."
    }

    $DotNetConfig = $Mode
    $DotNetTargets = @(
        @{ Project = $CLIDir; RID = $RID },
        @{ Project = $TerminalDir; RID = $RID }
    )

    foreach ($target in $DotNetTargets) {
        $binDir = Join-Path $target.Project "bin/$DotNetConfig/net10.0/$($target.RID)"
        if (-not (Test-Path $binDir)) {
            New-Item -ItemType Directory -Path $binDir -Force | Out-Null
        }

        # Copy native library (name already platform-specific)
        $dest = Join-Path $binDir $LibName
        Copy-Item $LibSource $dest -Force
        Write-Host "  -> $dest"
    }

    Write-Success "Native library deployment complete"

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

    # Copy native lib to artifacts output directories
    $ArtifactCLI = Join-Path $Root "artifacts/bin/Hartonomous.CLI/$($DotNetConfig.ToLower())"
    $ArtifactTerminal = Join-Path $Root "artifacts/bin/Hartonomous.Terminal/$($DotNetConfig.ToLower())"
    
    foreach ($dir in @($ArtifactCLI, $ArtifactTerminal)) {
        if (Test-Path $dir) {
            Copy-Item $LibSource $dir -Force
            Write-Host "Copied native lib to $dir"
        }
    }

    # =========================================================================
    # Tests
    # =========================================================================
    if ($Test) {
        Write-Step "Running Native Tests"
        
        $TestExeName = if ($IsWindows) { "hartonomous-tests.exe" } else { "hartonomous-tests" }
        $TestExe = Join-Path $BuildDir "bin/$TestExeName"
        if (Test-Path $TestExe) {
            & $TestExe
            if ($LASTEXITCODE -ne 0) { throw "Native tests failed" }
            Write-Success "Native tests passed"
        } else {
            Write-Warning "Native test executable not found: $TestExe"
        }

        Write-Step "Running .NET Tests"
        dotnet test "$Root/Hartonomous.slnx" --configuration $DotNetConfig --no-build
        if ($LASTEXITCODE -ne 0) { throw ".NET tests failed" }
        Write-Success ".NET tests passed"
    }

    # =========================================================================
    # Database Seeding
    # =========================================================================
    if ($Seed) {
        Write-Step "Seeding Database"

        $SeedExeName = if ($IsWindows) { "hartonomous-seed.exe" } else { "hartonomous-seed" }
        $SeedExe = Join-Path $BuildDir "bin/$SeedExeName"
        if (Test-Path $SeedExe) {
            if (-not $env:HARTONOMOUS_DB_URL) {
                # Default aligns with docker-compose/Aspire dev port mapping (host 5433 -> container 5432)
                $env:HARTONOMOUS_DB_URL = "postgresql://hartonomous:hartonomous@localhost:5433/hartonomous"
            }
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
    $CLIExe = if ($IsWindows) { "hartonomous.exe" } else { "hartonomous" }
    $TerminalExe = if ($IsWindows) { "hartonomous-terminal.exe" } else { "hartonomous-terminal" }
    Write-Host "Run CLI:      ./Hartonomous.CLI/bin/$DotNetConfig/net10.0/$RID/$CLIExe"
    Write-Host "Run Terminal: ./Hartonomous.Terminal/bin/$DotNetConfig/net10.0/$RID/$TerminalExe"
    Write-Host ""

} catch {
    Write-Host "`n" -NoNewline
    Write-Host "========================================" -ForegroundColor Red
    Write-Host " BUILD FAILED " -ForegroundColor Red -BackgroundColor DarkRed
    Write-Host "========================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
