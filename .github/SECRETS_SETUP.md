# GitHub Secrets Setup

This document describes the required GitHub Secrets for CI/CD pipeline execution.

## Required Secrets

### Test Environment Secrets

These secrets are used for running integration tests in GitHub Actions CI:

| Secret Name | Description | Example Value | Required |
|-------------|-------------|---------------|----------|
| `TEST_POSTGRES_USER` | PostgreSQL test database username | `hartonomous_ci` | Yes |
| `TEST_POSTGRES_PASSWORD` | PostgreSQL test database password | Generate with `openssl rand -base64 32` | Yes |
| `TEST_POSTGRES_DB` | PostgreSQL test database name | `hartonomous_test` | Yes |
| `TEST_NEO4J_USER` | Neo4j test database username | `neo4j` | Yes |
| `TEST_NEO4J_PASSWORD` | Neo4j test database password | Generate with `openssl rand -base64 32` | Yes |

### Azure Deployment Secrets

These secrets are used for deploying to Azure environments:

| Secret Name | Description | Required |
|-------------|-------------|----------|
| `AZURE_CLIENT_ID` | Azure Service Principal Client ID | Yes |
| `AZURE_TENANT_ID` | Azure Active Directory Tenant ID | Yes |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID | Yes |

## How to Set Up Secrets

### Using GitHub Web UI

1. Navigate to your repository on GitHub
2. Click on **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Enter the secret name and value
5. Click **Add secret**
6. Repeat for each required secret

### Using GitHub CLI

```bash
# Generate secure random passwords
TEST_POSTGRES_PASSWORD=$(openssl rand -base64 32)
TEST_NEO4J_PASSWORD=$(openssl rand -base64 32)

# Set test environment secrets
gh secret set TEST_POSTGRES_USER --body "hartonomous_ci"
gh secret set TEST_POSTGRES_PASSWORD --body "$TEST_POSTGRES_PASSWORD"
gh secret set TEST_POSTGRES_DB --body "hartonomous_test"
gh secret set TEST_NEO4J_USER --body "neo4j"
gh secret set TEST_NEO4J_PASSWORD --body "$TEST_NEO4J_PASSWORD"

# Set Azure deployment secrets (replace with your actual values)
gh secret set AZURE_CLIENT_ID --body "your-client-id-here"
gh secret set AZURE_TENANT_ID --body "your-tenant-id-here"
gh secret set AZURE_SUBSCRIPTION_ID --body "your-subscription-id-here"
```

## Security Best Practices

1. **Never commit secrets to version control**
   - Secrets are stored securely in GitHub's encrypted storage
   - Workflow logs automatically redact secret values

2. **Use strong, randomly generated passwords**
   - Use `openssl rand -base64 32` or similar tools
   - Minimum 32 characters recommended

3. **Rotate secrets regularly**
   - Implement a secret rotation policy (e.g., every 90 days)
   - Update both GitHub Secrets and any deployed services

4. **Separate test and production credentials**
   - Test credentials (TEST_*) are only for CI/CD testing
   - Production credentials should be stored in Azure Key Vault

5. **Use environment-specific secrets**
   - Development, Staging, and Production should have separate secrets
   - Use GitHub Environments for environment-specific secrets

6. **Principle of least privilege**
   - Test database users should have minimal required permissions
   - Production service principals should have scoped access

## Verification

After setting up secrets, verify they're configured correctly:

```bash
# List all secrets (values are hidden)
gh secret list

# Expected output:
# TEST_POSTGRES_USER       Updated YYYY-MM-DD
# TEST_POSTGRES_PASSWORD   Updated YYYY-MM-DD
# TEST_POSTGRES_DB         Updated YYYY-MM-DD
# TEST_NEO4J_USER         Updated YYYY-MM-DD
# TEST_NEO4J_PASSWORD     Updated YYYY-MM-DD
# AZURE_CLIENT_ID         Updated YYYY-MM-DD
# AZURE_TENANT_ID         Updated YYYY-MM-DD
# AZURE_SUBSCRIPTION_ID   Updated YYYY-MM-DD
```

## Troubleshooting

### CI fails with "Secret not found" error

**Problem**: Workflow cannot access required secrets

**Solution**:
1. Verify secret name matches exactly (case-sensitive)
2. Check secret is set at repository level (not environment level)
3. Ensure workflow has correct permissions in repository settings

### Database connection fails in CI

**Problem**: PostgreSQL or Neo4j connection refused

**Solution**:
1. Verify service definitions in workflow match secret credentials
2. Check health checks are passing for database services
3. Ensure POSTGRES_USER/POSTGRES_PASSWORD in service env match TEST_* secrets

### Azure deployment fails with authentication error

**Problem**: Azure Login step fails

**Solution**:
1. Verify Service Principal exists and is not expired
2. Check RBAC permissions for subscription
3. Ensure Tenant ID and Subscription ID are correct
4. Verify federated credentials are configured for GitHub Actions

## Additional Resources

- [GitHub Actions Security Hardening](https://docs.github.com/en/actions/security-guides/security-hardening-for-github-actions)
- [Using Secrets in GitHub Actions](https://docs.github.com/en/actions/security-guides/using-secrets-in-github-actions)
- [Azure Service Principals](https://learn.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal)
