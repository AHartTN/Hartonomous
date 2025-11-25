#Requires -Version 5.1
<#
.SYNOPSIS
    Helper script for creating standardized Git commit messages
.DESCRIPTION
    Creates a properly formatted commit message file in .gitmsg/ directory
    following the project's commit message convention
.PARAMETER Type
    Commit type (feat, fix, docs, style, refactor, perf, test, chore, ci)
.PARAMETER Subject
    Brief description of the change (will be used for filename)
.EXAMPLE
    .\commit-helper.ps1 -Type "feat" -Subject "Add GPU acceleration"
.NOTES
    Author: Anthony Hart
    See .gitmsg/CONVENTION.md for full guidelines
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("feat","fix","docs","style","refactor","perf","test","chore","ci")]
    [string]$Type,
    
    [Parameter(Mandatory=$true)]
    [string]$Subject,
    
    [Parameter(Mandatory=$false)]
    [string]$Body = "",
    
    [Parameter(Mandatory=$false)]
    [string]$Footer = ""
)

$ErrorActionPreference = "Stop"

# Create .gitmsg directory if it doesn't exist
if (-not (Test-Path ".gitmsg")) {
    New-Item -Path ".gitmsg" -ItemType Directory -Force | Out-Null
}

# Generate filename
$timestamp = Get-Date -Format "yyyyMMdd-HHmm"
$scope = ($Subject -split " ")[0].ToLower() -replace "[^a-z0-9-]", ""
$filename = ".gitmsg/$Type-$scope-$timestamp.txt"

# Create commit message content
$content = "$Type`: $Subject`n"

if ($Body) {
    $content += "`n$Body`n"
}

if ($Footer) {
    $content += "`n$Footer`n"
}

# Write to file
$content | Out-File -FilePath $filename -Encoding UTF8 -NoNewline

Write-Host "Created commit message file: $filename" -ForegroundColor Green
Write-Host "`nOpening in VS Code..." -ForegroundColor Cyan

# Open in VS Code
code $filename

Write-Host "`nWhen ready to commit:" -ForegroundColor Yellow
Write-Host "  git add -A" -ForegroundColor White
Write-Host "  git commit -F $filename" -ForegroundColor White
Write-Host "  git push origin main" -ForegroundColor White
