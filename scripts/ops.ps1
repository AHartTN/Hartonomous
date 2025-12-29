<#
.SYNOPSIS
  Hartonomous developer operations entrypoint.

.DESCRIPTION
  Idempotent wrapper over existing repo scripts and common local-dev operations.

  This script intentionally does not invent new behavior; it standardizes entrypoints:
  - build.ps1 (root)
  - validate.ps1 (root)
  - docker compose up/down
  - dotnet build/run for AppHost

.EXAMPLE
  pwsh ./scripts/ops.ps1 -Action Build -Mode Debug
  pwsh ./scripts/ops.ps1 -Action Build -Mode Release -Clean -Test
  pwsh ./scripts/ops.ps1 -Action Validate
  pwsh ./scripts/ops.ps1 -Action DockerUp
  pwsh ./scripts/ops.ps1 -Action RunAppHost -Mode Debug
#>

[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        "Build",
        "Validate",
        "DockerUp",
        "DockerDown",
        "BuildAppHost",
        "RunAppHost"
    )]
    [string]$Action,

    [ValidateSet("Debug", "Release")]
    [string]$Mode = "Debug",

    [switch]$Clean,
    [switch]$Test,
    [switch]$Seed,
    [switch]$SkipNative
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".."))
$BuildScript = Join-Path $RepoRoot "build.ps1"
$ValidateScript = Join-Path $RepoRoot "validate.ps1"
$AppHostProject = Join-Path $RepoRoot "Hartonomous.AppHost\Hartonomous.AppHost.csproj"

function Invoke-External([string]$Exe, [string[]]$ArgumentList) {
    Write-Host "`n> $Exe $($ArgumentList -join ' ')" -ForegroundColor DarkGray
    & $Exe @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $Exe"
    }
}

switch ($Action) {
    "Build" {
        if (-not (Test-Path $BuildScript)) {
            throw "Missing build script: $BuildScript"
        }

        $args = @()
        $args += "-Mode"; $args += $Mode
        if ($Clean) { $args += "-Clean" }
        if ($Test) { $args += "-Test" }
        if ($Seed) { $args += "-Seed" }
        if ($SkipNative) { $args += "-SkipNative" }

        Invoke-External "pwsh" (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $BuildScript) + $args)
        break
    }

    "Validate" {
        if (-not (Test-Path $ValidateScript)) {
            throw "Missing validate script: $ValidateScript"
        }
        Invoke-External "pwsh" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ValidateScript)
        break
    }

    "DockerUp" {
        Push-Location $RepoRoot
        try {
            Invoke-External "docker" @("compose", "up", "-d", "postgres", "redis")
        }
        finally {
            Pop-Location
        }
        break
    }

    "DockerDown" {
        Push-Location $RepoRoot
        try {
            Invoke-External "docker" @("compose", "down")
        }
        finally {
            Pop-Location
        }
        break
    }

    "BuildAppHost" {
        if (-not (Test-Path $AppHostProject)) {
            throw "Missing AppHost project: $AppHostProject"
        }
        Invoke-External "dotnet" @("build", $AppHostProject, "-c", $Mode)
        break
    }

    "RunAppHost" {
        if (-not (Test-Path $AppHostProject)) {
            throw "Missing AppHost project: $AppHostProject"
        }
        # Running AppHost is the canonical Aspire dev loop.
        Invoke-External "dotnet" @("run", "--project", $AppHostProject, "-c", $Mode)
        break
    }

    default {
        throw "Unhandled action: $Action"
    }
}
