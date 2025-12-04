# GitHub Actions Workflows

This directory contains all CI/CD workflows for the Hartonomous project.

## Workflow Overview

### Continuous Integration (CI)

#### ci-test.yml
**Purpose**: Run unit and integration tests on every push and PR

**Triggers**:
- Push to `develop`, `staging`, `main` branches
- Pull requests to `develop`, `staging`, `main` branches
- Manual workflow dispatch

**Jobs**:
1. **Unit Tests** - Run on Python 3.10, 3.11, 3.12
   - Install dependencies from `requirements.txt`
   - Run pytest with coverage
   - Upload coverage reports to Codecov

2. **Integration Tests** - Run with PostgreSQL and Neo4j services
   - Set up test database
   - Run integration tests
   - Validate API endpoints

**Environment Variables Required**:
- `PGPASSWORD` - PostgreSQL password for test database

---

#### ci-lint.yml
**Purpose**: Code quality checks and linting

**Triggers**:
- Push to all branches
- Pull requests

**Jobs**:
1. **Lint Python** - Run ruff/black/isort
2. **Lint YAML** - Validate workflow files
3. **Lint SQL** - Check database migrations
4. **Lint Markdown** - Documentation quality

---

#### ci-security.yml
**Purpose**: Security scanning and vulnerability detection

**Triggers**:
- Push to `main` branch
- Scheduled daily runs
- Manual workflow dispatch

**Jobs**:
1. **SAST Scan (Bandit)** - Python security analysis
2. **Dependency Scan** - Check for vulnerable dependencies
3. **Secret Scanning** - Detect hardcoded secrets

---

#### pylint.yml
**Purpose**: Code quality analysis with Pylint

**Triggers**:
- Push to all branches

**Configuration**:
- Uses `.pylintrc` for configuration
- Runs on Python 3.8, 3.9, 3.10
- Allows failures (informational only)

---

### Continuous Deployment (CD)

#### cd-deploy-development.yml
**Purpose**: Deploy to HART-DESKTOP development environment

**Trigger**: Push to `develop` branch

**Target**: Self-hosted Windows runner (HART-DESKTOP)

**Jobs**:
1. Preflight checks
2. Deploy database schema
3. Deploy API application
4. Deploy Neo4j worker
5. Validation and smoke tests

**Secrets Required**:
- `AZURE_TENANT_ID`
- `AZURE_CLIENT_ID`
- `AZURE_CLIENT_SECRET`
- `KEY_VAULT_URL`

---

#### cd-deploy-staging.yml
**Purpose**: Deploy to hart-server staging environment

**Trigger**: Push to `staging` branch

**Target**: Self-hosted Linux runner (hart-server)

**Deployment Path**: `/srv/www/staging`

**Jobs**:
1. Preflight checks
2. Deploy database schema
3. Deploy API to `/srv/www/staging`
4. Deploy Neo4j worker
5. Validation and smoke tests

---

#### cd-deploy-production.yml
**Purpose**: Deploy to hart-server production environment

**Trigger**: Manual workflow dispatch with version input

**Target**: Self-hosted Linux runner (hart-server)

**Deployment Path**: `/srv/www/production`

**Jobs**:
1. **Manual Approval** - Requires environment approval
2. Preflight checks
3. Backup current production
4. Deploy database schema
5. Deploy API to `/srv/www/production`
6. Deploy Neo4j worker
7. Validation and smoke tests
8. Create GitHub Release

**Required Inputs**:
- `version` - Version tag to deploy (e.g., v1.2.3)
- `skip_tests` - Skip validation tests (optional)

---

### Community Workflows

#### community-coffee.yml
**Purpose**: Buy Me a Coffee integration

#### community-issue-management.yml
**Purpose**: Automated issue triage and labeling

#### community-pr-management.yml
**Purpose**: PR automation and checks

#### community-sponsor.yml
**Purpose**: GitHub Sponsors integration

---

### CIAM (Customer Identity Access Management)

#### ciam-user-provisioning.yml
**Purpose**: Azure AD B2C / Entra External ID user provisioning

**Trigger**: Manual workflow dispatch

**Features**:
- Create test users in B2C tenant
- Deploy custom policies
- Cleanup test users

---

## Self-Hosted Runners

### HART-DESKTOP (Development)
**OS**: Windows 11
**Labels**: `self-hosted`, `windows`, `HART-DESKTOP`
**Purpose**: Development environment deployments

**Setup**:
```powershell
# Download runner
Invoke-WebRequest -Uri https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-win-x64-2.311.0.zip -OutFile actions-runner-win-x64-2.311.0.zip

# Extract
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory("$PWD\actions-runner-win-x64-2.311.0.zip", "$PWD")

# Configure
.\config.cmd --url https://github.com/AHartTN/Hartonomous --token YOUR_TOKEN --labels self-hosted,windows,HART-DESKTOP

# Run as service
.\svc.cmd install
.\svc.cmd start
```

---

### hart-server (Staging/Production)
**OS**: Ubuntu Linux
**Labels**: `self-hosted`, `linux`, `hart-server`
**Purpose**: Staging and production deployments

**Setup**:
```bash
# Download runner
curl -o actions-runner-linux-x64-2.311.0.tar.gz -L https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-linux-x64-2.311.0.tar.gz

# Extract
tar xzf ./actions-runner-linux-x64-2.311.0.tar.gz

# Configure
./config.sh --url https://github.com/AHartTN/Hartonomous --token YOUR_TOKEN --labels self-hosted,linux,hart-server

# Install as service
sudo ./svc.sh install
sudo ./svc.sh start
```

---

## Required GitHub Secrets

Configure these secrets in GitHub repository settings:

### Azure Authentication
- `AZURE_TENANT_ID` - Microsoft Entra ID tenant ID
- `AZURE_CLIENT_ID` - Service principal client ID (development/staging)
- `AZURE_CLIENT_SECRET` - Service principal client secret (development/staging)
- `AZURE_CLIENT_ID_PROD` - Service principal client ID (production)
- `AZURE_CLIENT_SECRET_PROD` - Service principal client secret (production)

### Azure Resources
- `KEY_VAULT_URL` - Azure Key Vault URL (https://kv-hartonomous.vault.azure.net)

### Optional
- `CODECOV_TOKEN` - Codecov.io token for coverage reports

---

## Deployment Flow

```
???????????????????
? Push to develop ?
???????????????????
         ?
         ???? CI Tests (unit + integration)
         ???? CI Lint (code quality)
         ???? CD Deploy to HART-DESKTOP (dev)
                ???? Smoke tests
```

```
???????????????????
? Push to staging ?
???????????????????
         ?
         ???? CI Tests
         ???? CI Lint
         ???? CD Deploy to hart-server (staging)
                ???? Backup
                ???? Deploy to /srv/www/staging
                ???? Smoke tests
```

```
?????????????????????????
? Manual trigger (main) ?
?????????????????????????
           ?
           ???? Approval required
           ???? Preflight checks
           ???? Backup production
           ???? CD Deploy to hart-server (production)
                  ???? Deploy to /srv/www/production
                  ???? Smoke tests
                  ???? Create GitHub Release
```

---

## Troubleshooting

### Test Failures
**Issue**: Tests failing due to missing test directory
**Solution**: Tests are now created automatically by `run-unit-tests.sh`

### Lint Failures
**Issue**: YAML lint errors for line length or indentation
**Solution**: Use `.yamllint` configuration with relaxed rules

### Security Scan Issues
**Issue**: Bandit reports hardcoded secrets or security issues
**Solution**: 
- Use `# nosec` comments for false positives
- Store secrets in Azure Key Vault
- Use environment variables

### Self-Hosted Runner Not Available
**Issue**: Workflow queued but never runs
**Solution**:
1. Check runner status: GitHub Settings ? Actions ? Runners
2. Restart runner service:
   - Windows: `.\svc.cmd restart`
   - Linux: `sudo ./svc.sh restart`
3. Check runner logs in `_diag` directory

---

## Best Practices

1. **Never commit secrets** - Use Azure Key Vault + GitHub Secrets
2. **Test locally first** - Run tests before pushing
3. **Use feature branches** - Merge to develop via PR
4. **Tag releases** - Use semantic versioning (v1.2.3)
5. **Monitor deployments** - Check workflow runs for errors
6. **Backup before deploy** - Always enabled in prod workflows

---

## References

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Self-Hosted Runners](https://docs.github.com/en/actions/hosting-your-own-runners)
- [Azure DevOps Integration](https://learn.microsoft.com/en-us/azure/devops/)
- [Deployment Best Practices](../docs/deployment/)
