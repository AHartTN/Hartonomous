# Azure Arc Integration for Hartonomous

## Overview

Leverage Azure Arc-connected on-premises machines (HART-SERVER, HART-DESKTOP) for hybrid development and deployment.

## Connected Machines

### HART-SERVER
- **Type:** On-premises server
- **Azure Arc:** Connected
- **SSH:** Enabled
- **Role:** Primary build/deployment server
- **Capabilities:** Docker, PostgreSQL, heavy compute workloads

### HART-DESKTOP
- **Type:** Development workstation
- **Azure Arc:** Connected  
- **SSH:** Enabled
- **Role:** Development and testing
- **Capabilities:** Visual Studio, testing, local development

---

## SSH Key Setup

### Generate SSH Keys (Already Done)

```powershell
# On HART-SERVER
ssh-keygen -t ed25519 -C "hart-server@hartonomous" -f ~/.ssh/hart-server

# On HART-DESKTOP  
ssh-keygen -t ed25519 -C "hart-desktop@hartonomous" -f ~/.ssh/hart-desktop
```

### Add to Azure DevOps

```bash
# Add SSH keys to Azure DevOps for Git operations
# Navigate to: https://dev.azure.com/aharttn/_usersSettings/keys

# Copy public key
cat ~/.ssh/hart-server.pub  # or hart-desktop.pub

# Add in Azure DevOps SSH Keys settings
```

### Configure Git for SSH

```bash
# On both machines
git config --global user.name "Your Name"
git config --global user.email "your.email@domain.com"

# Test SSH connection
ssh -T git@ssh.dev.azure.com

# Clone with SSH
git clone git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous
```

---

## Azure Pipelines Self-Hosted Agents

### Install Agent on HART-SERVER

```powershell
# Create agent directory
mkdir D:\azagent ; cd D:\azagent

# Download agent
Invoke-WebRequest -Uri "https://vstsagentpackage.azureedge.net/agent/3.236.1/vsts-agent-win-x64-3.236.1.zip" -OutFile agent.zip

# Extract
Expand-Archive -Path agent.zip -DestinationPath .

# Configure agent
.\config.cmd `
    --unattended `
    --url "https://dev.azure.com/aharttn" `
    --auth pat `
    --token $env:AZURE_DEVOPS_PAT `
    --pool "Hartonomous-OnPrem" `
    --agent "HART-SERVER" `
    --work "_work" `
    --runAsService

# Install as Windows service
.\config.cmd --runAsService
```

### Install Agent on HART-DESKTOP

```powershell
# Same steps as HART-SERVER
mkdir D:\azagent ; cd D:\azagent

# Download and configure
# Use agent name "HART-DESKTOP"
```

### Create Agent Pool

```bash
# Create custom agent pool
az pipelines pool create \
    --name "Hartonomous-OnPrem" \
    --pool-type private \
    --org https://dev.azure.com/aharttn \
    --project Hartonomous

# Grant permissions
az pipelines pool permission set \
    --pool-name "Hartonomous-OnPrem" \
    --allow-pipelines true \
    --org https://dev.azure.com/aharttn \
    --project Hartonomous
```

---

## Hybrid Build Pipeline

### On-Prem Build Pipeline

**File:** `.azure-pipelines/build-onprem.yml`

```yaml
trigger:
  branches:
    include:
      - main
      - develop
  paths:
    include:
      - Hartonomous.Core/**
      - Hartonomous.Infrastructure/**
      - Hartonomous.API/**

# Use self-hosted agent pool
pool:
  name: Hartonomous-OnPrem
  demands:
    - Agent.Name -equals HART-SERVER

variables:
  buildConfiguration: 'Release'
  buildPlatform: 'Any CPU'

steps:
  - checkout: self
    clean: true
    fetchDepth: 1

  - task: UseDotNet@2
    inputs:
      version: '10.x'
      includePreviewVersions: true
    displayName: 'Install .NET 10'

  - task: DotNetCoreCLI@2
    inputs:
      command: 'restore'
      projects: '**/*.csproj'
    displayName: 'Restore NuGet Packages'

  - task: DotNetCoreCLI@2
    inputs:
      command: 'build'
      projects: '**/*.csproj'
      arguments: '--configuration $(buildConfiguration) --no-restore'
    displayName: 'Build Projects'

  - task: DotNetCoreCLI@2
    inputs:
      command: 'test'
      projects: '**/*Tests/*.csproj'
      arguments: '--configuration $(buildConfiguration) --no-build --logger trx'
    displayName: 'Run Tests'

  - task: PublishTestResults@2
    inputs:
      testResultsFormat: 'VSTest'
      testResultsFiles: '**/*.trx'
    displayName: 'Publish Test Results'

  - task: DotNetCoreCLI@2
    inputs:
      command: 'publish'
      publishWebProjects: false
      projects: |
        Hartonomous.API/Hartonomous.API.csproj
        Hartonomous.Worker/Hartonomous.Worker.csproj
      arguments: '--configuration $(buildConfiguration) --output $(Build.ArtifactStagingDirectory)'
      zipAfterPublish: true
    displayName: 'Publish Applications'

  - task: PublishBuildArtifacts@1
    inputs:
      PathtoPublish: '$(Build.ArtifactStagingDirectory)'
      ArtifactName: 'drop'
    displayName: 'Publish Build Artifacts'

  # Performance benchmarks on dedicated hardware
  - task: DotNetCoreCLI@2
    inputs:
      command: 'run'
      projects: 'Hartonomous.Benchmarks/Hartonomous.Benchmarks.csproj'
      arguments: '--configuration Release -- --job short --exporters json'
    displayName: 'Run Performance Benchmarks'
    condition: eq(variables['Build.SourceBranch'], 'refs/heads/main')

  - task: PublishBuildArtifacts@1
    inputs:
      PathtoPublish: 'BenchmarkDotNet.Artifacts/results'
      ArtifactName: 'benchmarks'
    displayName: 'Publish Benchmark Results'
    condition: eq(variables['Build.SourceBranch'], 'refs/heads/main')
```

---

## Local PostgreSQL + PostGIS Setup

### HART-SERVER PostgreSQL Configuration

```powershell
# Install PostgreSQL 16 with PostGIS
winget install PostgreSQL.PostgreSQL.16

# Install PostGIS extension
# Download from: https://download.osgeo.org/postgis/windows/

# Configure for development
$pgData = "D:\PostgreSQL\16\data"
$pgConf = "$pgData\postgresql.conf"

# Edit postgresql.conf
@"
# Hartonomous optimizations
shared_buffers = 8GB
effective_cache_size = 24GB
maintenance_work_mem = 2GB
work_mem = 128MB
max_parallel_workers_per_gather = 4
max_parallel_workers = 8
max_worker_processes = 8
random_page_cost = 1.1
effective_io_concurrency = 200
"@ | Add-Content -Path $pgConf

# Restart PostgreSQL
Restart-Service postgresql-x64-16

# Create database
psql -U postgres -c "CREATE DATABASE hartonomous;"
psql -U postgres -d hartonomous -c "CREATE EXTENSION postgis;"
psql -U postgres -d hartonomous -c "CREATE EXTENSION postgis_topology;"
```

### Connection String for Pipelines

```yaml
variables:
  - group: Hartonomous-Secrets  # Secure variable group
  
  # Connection to HART-SERVER PostgreSQL
  connectionString: 'Host=HART-SERVER;Port=5432;Database=hartonomous;Username=$(DB_USER);Password=$(DB_PASSWORD)'
```

---

## Deployment Pipeline

### Deploy to HART-SERVER

**File:** `.azure-pipelines/deploy-onprem.yml`

```yaml
trigger: none  # Manual deployment

pool:
  name: Hartonomous-OnPrem
  demands:
    - Agent.Name -equals HART-SERVER

variables:
  - group: Hartonomous-Secrets

stages:
  - stage: Deploy
    displayName: 'Deploy to HART-SERVER'
    jobs:
      - deployment: DeployAPI
        displayName: 'Deploy API'
        environment: 'HART-SERVER-Production'
        strategy:
          runOnce:
            deploy:
              steps:
                - download: current
                  artifact: drop

                - task: PowerShell@2
                  inputs:
                    targetType: 'inline'
                    script: |
                      # Stop existing services
                      Stop-Service -Name "Hartonomous.API" -ErrorAction SilentlyContinue
                      Stop-Service -Name "Hartonomous.Worker" -ErrorAction SilentlyContinue
                      
                      # Deploy new versions
                      $apiPath = "D:\Applications\Hartonomous\API"
                      $workerPath = "D:\Applications\Hartonomous\Worker"
                      
                      Remove-Item -Path $apiPath\* -Recurse -Force
                      Remove-Item -Path $workerPath\* -Recurse -Force
                      
                      Expand-Archive -Path "$(Pipeline.Workspace)\drop\Hartonomous.API.zip" -DestinationPath $apiPath
                      Expand-Archive -Path "$(Pipeline.Workspace)\drop\Hartonomous.Worker.zip" -DestinationPath $workerPath
                      
                      # Start services
                      Start-Service -Name "Hartonomous.API"
                      Start-Service -Name "Hartonomous.Worker"
                      
                      Write-Host "Deployment complete!"
                  displayName: 'Deploy Applications'

                - task: PowerShell@2
                  inputs:
                    targetType: 'inline'
                    script: |
                      # Run EF Core migrations
                      cd D:\Applications\Hartonomous\API
                      
                      dotnet ef database update `
                        --connection "$(connectionString)" `
                        --project Hartonomous.Data.dll
                  displayName: 'Run Database Migrations'

                - task: PowerShell@2
                  inputs:
                    targetType: 'inline'
                    script: |
                      # Health check
                      $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -UseBasicParsing
                      
                      if ($response.StatusCode -eq 200) {
                        Write-Host "##vso[task.complete result=Succeeded;]API is healthy"
                      }
                      else {
                        Write-Host "##vso[task.complete result=Failed;]API health check failed"
                        exit 1
                      }
                  displayName: 'Health Check'
```

---

## Development Workflow

### Work on HART-DESKTOP

```powershell
# Clone repo with SSH
cd D:\Repositories
git clone git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous

cd Hartonomous

# Create feature branch
git checkout -b feature/hilbert-encoder

# Make changes in Visual Studio
# ...

# Commit and push
git add .
git commit -m "Implement Hilbert curve encoder"
git push origin feature/hilbert-encoder

# Create PR in Azure DevOps
az repos pr create `
    --title "Implement Hilbert Curve Encoder" `
    --description "Adds SIMD-optimized Hilbert encoding" `
    --source-branch feature/hilbert-encoder `
    --target-branch develop `
    --org https://dev.azure.com/aharttn `
    --project Hartonomous
```

### Build and Test on HART-SERVER

Pipeline triggers automatically when PR is created. Self-hosted agent (HART-SERVER) builds and runs tests.

### Merge and Deploy

After PR approval, merge triggers deployment pipeline. Application deploys to HART-SERVER.

---

## Remote Development

### SSH into HART-SERVER from HART-DESKTOP

```powershell
# SSH connection
ssh username@HART-SERVER

# Or use VS Code Remote SSH
code --remote ssh-remote+HART-SERVER /d/Repositories/Hartonomous
```

### Remote Debugging

Configure Visual Studio for remote debugging:

1. Install Remote Debugger on HART-SERVER
2. Configure firewall rules
3. Attach debugger from HART-DESKTOP

```powershell
# Start remote debugger on HART-SERVER
msvsmon.exe /anyuser /nosecuritywarn

# In Visual Studio on HART-DESKTOP
# Debug ? Attach to Process
# Connection Type: Remote (no authentication)
# Connection Target: HART-SERVER:4026
```

---

## Monitoring and Observability

### Application Insights

```yaml
# Add to appsettings.json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=your-key-here"
  }
}
```

### Custom Telemetry Dashboard

```powershell
# Create Azure Dashboard for HART-SERVER metrics
az portal dashboard create `
    --name "Hartonomous-OnPrem-Metrics" `
    --resource-group "Hartonomous-RG" `
    --input-path "./dashboard-config.json"
```

---

## Security

### Secure Secrets

```powershell
# Store connection strings in Azure Key Vault
az keyvault secret set `
    --vault-name "hartonomous-kv" `
    --name "PostgreSQL-ConnectionString" `
    --value "Host=HART-SERVER;Port=5432;Database=hartonomous;Username=hart_user;Password=***"

# Reference in pipeline
variables:
  - group: Hartonomous-Secrets  # Links to Key Vault
```

### Network Security

```powershell
# Configure Windows Firewall on HART-SERVER
New-NetFirewallRule -DisplayName "PostgreSQL" -Direction Inbound -LocalPort 5432 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "Hartonomous API" -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "SSH" -Direction Inbound -LocalPort 22 -Protocol TCP -Action Allow
```

---

## Backup Strategy

### Automated PostgreSQL Backups

```powershell
# Backup script for HART-SERVER
$backupPath = "D:\Backups\PostgreSQL"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = "$backupPath\hartonomous-$timestamp.backup"

# Create backup
& "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe" `
    -h localhost `
    -U postgres `
    -F c `
    -b `
    -v `
    -f $backupFile `
    hartonomous

# Upload to Azure Blob Storage
az storage blob upload `
    --account-name hartonomousbackups `
    --container-name postgresql `
    --name "hartonomous-$timestamp.backup" `
    --file $backupFile
```

Schedule with Task Scheduler:
```powershell
$action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File D:\Scripts\backup-postgres.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At "2:00AM"
Register-ScheduledTask -Action $action -Trigger $trigger -TaskName "PostgreSQL Backup" -Description "Daily backup of Hartonomous database"
```

---

## Benefits of Hybrid Setup

1. **Cost Savings**: Leverage existing on-prem hardware for builds/tests
2. **Performance**: Dedicated PostgreSQL on HART-SERVER (no Azure SQL costs)
3. **Security**: Sensitive data stays on-premises
4. **Flexibility**: Azure Arc enables hybrid management
5. **Development Speed**: Local LAN speeds between machines
6. **GPU Access**: Can use HART-SERVER GPU for benchmarks without Azure costs

---

## Next Steps

1. Install self-hosted agents on both machines
2. Create `Hartonomous-OnPrem` agent pool in Azure DevOps
3. Configure PostgreSQL on HART-SERVER
4. Set up Windows Services for API and Worker
5. Configure automated backups
6. Test deployment pipeline

