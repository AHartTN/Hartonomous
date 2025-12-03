# GitHub Container Registry (GHCR) Setup - Zero Trust

## Credentials Stored in Key Vault ✅

Your GitHub credentials are securely stored in Azure Key Vault:
- ✅ `github-username` - GitHub username (fetched dynamically)
- ✅ `github-token` - GitHub PAT (fetched dynamically, never logged)

## Zero Trust Authentication Flow

### Build Pipeline (Azure DevOps Agent)
1. Agent authenticates to Azure using service connection managed identity
2. Fetches GitHub username from Key Vault: `kv-hartonomous/github-username`
3. Pipes GitHub token directly from Key Vault to `docker login` (never stored in variable)
4. Pushes images to `ghcr.io/ahartn/hartonomous-api`

### Deploy Pipeline (Arc Machines)
1. Arc machine authenticates to Azure using its SystemAssigned managed identity
2. Fetches GitHub username from Key Vault
3. Pipes GitHub token directly from Key Vault to `docker login`
4. Pulls images from GHCR

## No Manual Configuration Required!

**Zero pipeline variables needed** - everything fetched dynamically from Key Vault.

## Updating GitHub Credentials

If you need to rotate your GitHub token:

```bash
# Rotate token and store in Key Vault
gh auth refresh -s write:packages
gh auth token | az keyvault secret set --vault-name kv-hartonomous --name github-token --value @-
```

The pipeline will automatically use the new token on the next run.

## Container Image URLs

Your images are pushed to:
```
ghcr.io/ahartn/hartonomous-api:latest
ghcr.io/ahartn/hartonomous-api:<build-id>
```

## Cost

**FREE!** GitHub Container Registry is free for:
- ✅ Unlimited public images
- ✅ 500 MB private storage free
- ✅ 1 GB data transfer/month free

## Make Images Public (Optional)

By default, images are private. To make public:
1. Go to `https://github.com/AHartTN?tab=packages`
2. Click on `hartonomous-api` package
3. Package settings → Change visibility → Public

This removes the need for authentication to pull images!
