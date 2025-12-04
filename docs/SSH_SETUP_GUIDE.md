# SSH Configuration Setup for Azure DevOps

## Quick Fix for HART-DESKTOP

Your HART-SERVER is working correctly (getting "Shell access is not supported" is the expected response).
HART-DESKTOP needs the SSH key configured properly.

### Step 1: Add SSH Key to Azure DevOps

On **HART-DESKTOP**, run:

```powershell
# Display your public key
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub

# Or if you used a custom name:
Get-Content $env:USERPROFILE\.ssh\hart-desktop.pub
```

**Copy the entire output** (starts with `ssh-ed25519 AAAA...`)

Then:
1. Go to: https://dev.azure.com/aharttn/_usersSettings/keys
2. Click **"+ New Key"**
3. Paste the public key
4. Name it: `HART-DESKTOP`
5. Click **"Add"**

### Step 2: Configure SSH Agent (Windows)

On **HART-DESKTOP**:

```powershell
# Start SSH Agent service
Set-Service ssh-agent -StartupType Automatic
Start-Service ssh-agent

# Add your SSH key to the agent
ssh-add $env:USERPROFILE\.ssh\id_ed25519

# Or if you used a custom name:
ssh-add $env:USERPROFILE\.ssh\hart-desktop
```

### Step 3: Create SSH Config File

On **HART-DESKTOP**, create `~/.ssh/config`:

```powershell
# Create the config file
$sshConfig = @"
Host ssh.dev.azure.com
    HostName ssh.dev.azure.com
    User git
    IdentityFile ~/.ssh/id_ed25519
    IdentitiesOnly yes
    StrictHostKeyChecking accept-new
"@

New-Item -Path "$env:USERPROFILE\.ssh\config" -ItemType File -Force -Value $sshConfig
```

If you used a custom key name (`hart-desktop`), update the `IdentityFile` line:
```
IdentityFile ~/.ssh/hart-desktop
```

### Step 4: Test Connection

```powershell
ssh -T git@ssh.dev.azure.com
```

**Expected response:**
```
remote: Shell access is not supported.
shell request failed on channel 0
```

This is **correct** - it means SSH authentication worked!

---

## Complete SSH Setup for Both Machines

### HART-SERVER (Linux)

Your server is already working! But here's the complete config for reference:

```bash
# Verify SSH key exists
ls -la ~/.ssh/

# Should see: id_ed25519 and id_ed25519.pub

# Add to SSH config
cat >> ~/.ssh/config << 'EOF'
Host ssh.dev.azure.com
    HostName ssh.dev.azure.com
    User git
    IdentityFile ~/.ssh/id_ed25519
    IdentitiesOnly yes
EOF

# Set correct permissions
chmod 600 ~/.ssh/config
chmod 600 ~/.ssh/id_ed25519
chmod 644 ~/.ssh/id_ed25519.pub

# Test
ssh -T git@ssh.dev.azure.com
```

### HART-DESKTOP (Windows)

Follow Steps 1-4 above.

---

## Clone Repository with SSH

Once both machines are configured:

### On HART-SERVER:

```bash
cd /home/ahart/repositories
git clone git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous
cd Hartonomous
```

### On HART-DESKTOP:

```powershell
cd D:\Repositories
git clone git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous
cd Hartonomous
```

---

## Troubleshooting

### "Permission denied (publickey)"

**Cause:** SSH key not added to Azure DevOps or not loaded in SSH agent.

**Fix:**
1. Verify key is in Azure DevOps: https://dev.azure.com/aharttn/_usersSettings/keys
2. Load key in agent:
   ```powershell
   ssh-add $env:USERPROFILE\.ssh\id_ed25519
   ```
3. Test with verbose output:
   ```powershell
   ssh -vT git@ssh.dev.azure.com
   ```

### "Could not open a connection to your authentication agent"

**Cause:** SSH agent not running (Windows).

**Fix:**
```powershell
Start-Service ssh-agent
ssh-add $env:USERPROFILE\.ssh\id_ed25519
```

### Key not found

**Cause:** SSH key file doesn't exist or wrong path.

**Fix:**
```powershell
# List keys
Get-ChildItem $env:USERPROFILE\.ssh\

# Generate new key if needed
ssh-keygen -t ed25519 -C "hart-desktop@hartonomous" -f $env:USERPROFILE\.ssh\id_ed25519
```

---

## Verify Setup

Run this on both machines:

```powershell
# Windows (HART-DESKTOP)
Write-Host "Testing SSH connection to Azure DevOps..." -ForegroundColor Cyan
ssh -T git@ssh.dev.azure.com

Write-Host "`nListing SSH keys..." -ForegroundColor Cyan
Get-ChildItem $env:USERPROFILE\.ssh\ | Select-Object Name

Write-Host "`nSSH agent status..." -ForegroundColor Cyan
Get-Service ssh-agent | Select-Object Status, StartType
```

```bash
# Linux (HART-SERVER)
echo "Testing SSH connection to Azure DevOps..."
ssh -T git@ssh.dev.azure.com

echo -e "\nListing SSH keys..."
ls -la ~/.ssh/

echo -e "\nSSH config..."
cat ~/.ssh/config
```

---

## Git Remote URLs

After cloning, verify you're using SSH:

```bash
cd Hartonomous
git remote -v
```

**Should show:**
```
origin  git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous (fetch)
origin  git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous (push)
```

If it shows HTTPS, update:
```bash
git remote set-url origin git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous
```

---

## Next Steps

Once SSH is working on both machines:

1. **Clone repository** on both machines
2. **Run setup script** on HART-DESKTOP:
   ```powershell
   cd D:\Repositories\Hartonomous
   .\setup-azure-devops.ps1
   ```
3. **Install self-hosted agents** (see AZURE_ARC_INTEGRATION.md)
4. **Configure PostgreSQL** on HART-SERVER
5. **Start development!**

