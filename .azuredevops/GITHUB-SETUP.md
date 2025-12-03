# GitHub Container Registry (GHCR) Setup

## You Already Have This Configured!

Your GitHub account (`AHartTN`) is authenticated with the necessary scopes:
- ✅ `repo` - Repository access
- ✅ `workflow` - GitHub Actions access
- ✅ Token authenticated via GitHub CLI

Your Azure service principals are already set up:
- ✅ `Hartonomous-GitHub-Actions-Development` (66a37c0f-5666-450b-b61f-c9e33b56115e)
- ✅ `Hartonomous-GitHub-Actions-Production` (48a904b7-f070-407d-abab-1b71a3c049a9)
- ✅ `Hartonomous-GitHub-Actions-Staging` (f05370b1-d09f-4085-bd04-ac028c28b7f8)

## One-Time Azure DevOps Configuration

### Add GitHub Token as Pipeline Variable

1. In Azure DevOps, go to **Pipelines** → **Library**
2. Create a new **Variable Group** named `GitHub`
3. Add these variables:
   - `GITHUB_USERNAME` = `AHartTN` (not secret)
   - `GITHUB_TOKEN` = Your GitHub PAT (click lock icon to make it secret)

4. Link to your pipeline:
   - Edit `azure-pipelines.yml`
   - Add at the top under `variables:`:
   ```yaml
   - group: GitHub
   ```

### Get Your GitHub Personal Access Token

Option 1: Use existing token from CLI
```bash
gh auth token
```

Option 2: Create new token
```bash
gh auth refresh -s write:packages
```

Option 3: Manual (GitHub.com)
1. Go to GitHub.com → Settings → Developer Settings → Personal Access Tokens → Tokens (classic)
2. Generate new token with scopes:
   - `read:packages`
   - `write:packages`
   - `delete:packages` (optional)
3. Copy token and add to Azure DevOps Library

## Container Image URL

Your images will be pushed to:
```
ghcr.io/ahartn/hartonomous-api:latest
ghcr.io/ahartn/hartonomous-api:<build-id>
```

## Cost

**FREE!** GitHub Container Registry is free for:
- ✅ Unlimited public images
- ✅ 500 MB private storage free
- ✅ 1 GB data transfer/month free

## Verify Setup

After configuring the variable group, test with:
```bash
docker login ghcr.io -u AHartTN -p $(gh auth token)
echo "If login succeeds, you're ready!"
```

## Make Images Public (Optional)

By default, images are private. To make public:
1. Go to `https://github.com/AHartTN?tab=packages`
2. Click on `hartonomous-api` package
3. Package settings → Change visibility → Public

This removes the need for authentication to pull images!
