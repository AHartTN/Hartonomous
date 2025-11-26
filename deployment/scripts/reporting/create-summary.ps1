# Create Deployment Summary
# Generates deployment report for GitHub Actions summary
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,
    
    [Parameter(Mandatory = $true)]
    [string]$GitRef,
    
    [Parameter(Mandatory = $true)]
    [string]$Actor
)

$timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC'

$summary = @"
## Deployment Summary

? Deployed to **$Environment** environment

- **Git Ref**: ``$GitRef``
- **Deployed by**: @$Actor
- **Timestamp**: $timestamp

### Components Deployed
- ? Database schema
- ? API application
- ? Service configuration

"@

# Write to GitHub Actions summary
if ($env:GITHUB_STEP_SUMMARY) {
    $summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding UTF8 -Append
}

Write-Host $summary
