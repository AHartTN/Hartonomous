# Azure DevOps SSH Setup for Windows
# Run this on HART-DESKTOP to configure SSH authentication

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Azure DevOps SSH Setup for HART-DESKTOP" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# Use existing RSA key for Azure DevOps
$sshKeyPath = "$env:USERPROFILE\.ssh\id_rsa_azure"
$sshPubKeyPath = "$sshKeyPath.pub"

if (-not (Test-Path $sshKeyPath)) {
    Write-Host "ERROR: SSH key not found at $sshKeyPath" -ForegroundColor Red
    Write-Host "Expected file: id_rsa_azure (RSA key for Azure DevOps)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Available keys in .ssh folder:" -ForegroundColor Yellow
    Get-ChildItem "$env:USERPROFILE\.ssh\" -Filter "id_*" | Select-Object Name | Format-Table -HideTableHeaders
    exit 1
}

Write-Host "? Found SSH key: id_rsa_azure" -ForegroundColor Green
Write-Host ""

# Verify key is already in Azure DevOps
Write-Host "Public key fingerprint:" -ForegroundColor Yellow
ssh-keygen -lf $sshPubKeyPath
Write-Host ""
Write-Host "This key should already be added to Azure DevOps at:" -ForegroundColor Gray
Write-Host "https://dev.azure.com/aharttn/_usersSettings/keys" -ForegroundColor White
Write-Host ""

# Configure SSH Agent
Write-Host "Configuring SSH Agent..." -ForegroundColor Cyan

try {
    # Set SSH Agent to start automatically
    Set-Service ssh-agent -StartupType Automatic -ErrorAction Stop
    
    # Start if not running
    $agentStatus = Get-Service ssh-agent
    if ($agentStatus.Status -ne 'Running') {
        Start-Service ssh-agent -ErrorAction Stop
        Write-Host "? SSH Agent started" -ForegroundColor Green
    }
    else {
        Write-Host "? SSH Agent already running" -ForegroundColor Green
    }
}
catch {
    Write-Host "? Could not configure SSH Agent: $_" -ForegroundColor Yellow
    Write-Host "You may need to run this script as Administrator" -ForegroundColor Yellow
}

# Add key to SSH agent
Write-Host ""
Write-Host "Adding key to SSH Agent..." -ForegroundColor Cyan

try {
    # Remove all keys first
    ssh-add -D 2>&1 | Out-Null
    
    # Add the Azure DevOps key
    $output = ssh-add $sshKeyPath 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? Key added to SSH Agent" -ForegroundColor Green
    }
    else {
        Write-Host "? Issue adding key: $output" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "? Could not add key to SSH Agent: $_" -ForegroundColor Yellow
}

# List loaded keys
Write-Host ""
Write-Host "Loaded SSH keys:" -ForegroundColor Cyan
ssh-add -l

# Create/Update SSH config
Write-Host ""
Write-Host "Creating SSH config..." -ForegroundColor Cyan

$sshConfig = @"
# Azure DevOps SSH Configuration
Host ssh.dev.azure.com
    HostName ssh.dev.azure.com
    User git
    IdentityFile ~/.ssh/id_rsa_azure
    IdentitiesOnly yes
    StrictHostKeyChecking accept-new

# HART-SERVER SSH Configuration
Host HART-SERVER
    HostName HART-SERVER
    User ahart
    IdentityFile ~/.ssh/id_rsa_azure
    StrictHostKeyChecking accept-new
"@

$sshConfigPath = "$env:USERPROFILE\.ssh\config"

if (Test-Path $sshConfigPath) {
    Write-Host "? SSH config already exists" -ForegroundColor Yellow
    
    $backup = Read-Host "Create backup and overwrite? (y/n)"
    if ($backup -eq 'y') {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        Copy-Item $sshConfigPath "$sshConfigPath.backup-$timestamp" -Force
        Write-Host "  Backup created: config.backup-$timestamp" -ForegroundColor Gray
        
        Set-Content -Path $sshConfigPath -Value $sshConfig -Force
        Write-Host "? SSH config updated" -ForegroundColor Green
    }
    else {
        Write-Host "  Skipping SSH config update" -ForegroundColor Gray
    }
}
else {
    Set-Content -Path $sshConfigPath -Value $sshConfig -Force
    Write-Host "? SSH config created" -ForegroundColor Green
}

# Test connection
Write-Host ""
Write-Host "Testing connection to Azure DevOps..." -ForegroundColor Cyan
Write-Host ""

$testResult = ssh -T git@ssh.dev.azure.com 2>&1

if ($testResult -like "*shell request failed*" -or $testResult -like "*Shell access is not supported*") {
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host " ? SSH AUTHENTICATION SUCCESSFUL!" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now use SSH with Azure DevOps:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Clone repository:" -ForegroundColor Yellow
    Write-Host "  cd D:\Repositories" -ForegroundColor White
    Write-Host "  git clone git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous" -ForegroundColor White
    Write-Host ""
    Write-Host "Or update existing repo to use SSH:" -ForegroundColor Yellow
    Write-Host "  cd Hartonomous" -ForegroundColor White
    Write-Host "  git remote set-url origin git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous" -ForegroundColor White
    Write-Host ""
}
elseif ($testResult -like "*Permission denied*") {
    Write-Host "================================================================" -ForegroundColor Red
    Write-Host " ? SSH AUTHENTICATION FAILED" -ForegroundColor Red
    Write-Host "================================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "1. Verify the key is added to Azure DevOps:" -ForegroundColor White
    Write-Host "   https://dev.azure.com/aharttn/_usersSettings/keys" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Check the public key matches:" -ForegroundColor White
    Write-Host "   Your public key:" -ForegroundColor Gray
    Get-Content $sshPubKeyPath | Write-Host -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Verify SSH agent has the key loaded:" -ForegroundColor White
    Write-Host "   ssh-add -l" -ForegroundColor Gray
    Write-Host ""
}
else {
    Write-Host "? Unexpected response:" -ForegroundColor Yellow
    Write-Host $testResult -ForegroundColor Gray
    Write-Host ""
    Write-Host "This might still work. Try cloning a repository to verify." -ForegroundColor Yellow
}

# Test connection to HART-SERVER
Write-Host ""
Write-Host "Testing connection to HART-SERVER..." -ForegroundColor Cyan

$serverTest = Test-Connection -ComputerName HART-SERVER -Count 1 -Quiet

if ($serverTest) {
    Write-Host "? HART-SERVER is reachable" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can SSH to HART-SERVER:" -ForegroundColor Cyan
    Write-Host "  ssh HART-SERVER" -ForegroundColor White
}
else {
    Write-Host "? HART-SERVER is not reachable" -ForegroundColor Yellow
    Write-Host "  Make sure HART-SERVER is powered on and connected to the network" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Setup Summary:" -ForegroundColor Cyan
Write-Host "  SSH Key: $(if (Test-Path $sshKeyPath) { '?' } else { '?' }) $sshKeyPath" -ForegroundColor White
Write-Host "  Public Key: $(if (Test-Path $sshPubKeyPath) { '?' } else { '?' }) $sshPubKeyPath" -ForegroundColor White
Write-Host "  SSH Config: $(if (Test-Path $sshConfigPath) { '?' } else { '?' }) $sshConfigPath" -ForegroundColor White
Write-Host "  SSH Agent: $(if ((Get-Service ssh-agent).Status -eq 'Running') { '? Running' } else { '? Not Running' })" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Update this repository to use SSH:" -ForegroundColor White
Write-Host "     git remote set-url origin git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Run Azure DevOps setup:" -ForegroundColor White
Write-Host "     .\setup-azure-devops.ps1" -ForegroundColor Gray
Write-Host ""
