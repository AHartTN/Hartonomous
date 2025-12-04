# Automated Certificate Management

## Zero-Touch Certificate Lifecycle

**Humans never see, touch, or manage certificates.** Everything is automated through Azure Key Vault with RBAC and managed identities.

## Setup (One-Time)

Run the setup script **once** to create infrastructure:

### Windows/PowerShell:
```powershell
.\scripts\setup-certificate.ps1
```

### Linux/macOS/Ubuntu Server (HART-SERVER):
```bash
chmod +x scripts/setup-certificate.sh
./scripts/setup-certificate.sh
```

### What It Does:
1. ? Creates Azure Resource Group (idempotent)
2. ? Creates Azure Key Vault (idempotent)
3. ? Generates self-signed certificate in Key Vault
4. ? Configures auto-rotation (90 days before expiry)
5. ? Sets access policies for your user
6. ? **No passwords, no manual steps, no human interaction**

## Pipeline Integration

The Azure DevOps pipeline automatically:

1. **Retrieves certificate** from Key Vault using managed identity
2. **Installs certificate** to build agent
3. **Signs NuGet packages** with certificate
4. **Timestamps signatures** (valid even after cert expires)
5. **Rotates certificate** automatically when needed

### Required Service Connection:

Create once in Azure DevOps:
```bash
# Navigate to: Project Settings ? Service connections ? New service connection
# Type: Azure Resource Manager
# Authentication: Managed Identity (recommended) or Service Principal
# Name: Azure-Service-Connection
# Grant access to: Hartonomous-RG and hartonomous-kv
```

## Certificate Rotation

**Fully Automated:**
- Certificate auto-renews 90 days before expiry
- No manual intervention required
- Pipeline always uses latest valid certificate
- Old certificates remain valid until expiry (timestamping)

## Security Features

### ? Zero Trust Architecture
- No passwords in code, config, or pipelines
- Managed identities for authentication
- RBAC for fine-grained access control
- Audit logs for all certificate operations

### ? Secrets Never Leave Key Vault
- Certificate retrieved on-demand during build
- Never stored in source control
- Never stored in build artifacts
- Automatically cleaned up after build

### ? Cross-Platform Support
- Works on Windows (HART-DESKTOP)
- Works on Linux (HART-SERVER Ubuntu)
- Works on Azure Pipelines agents
- Same script, same process, everywhere

## Manual Certificate Operations (For Reference Only)

You should **never need** these, but for emergency access:

### View Certificate
```bash
az keyvault certificate show \
    --vault-name hartonomous-kv \
    --name HartIndustries-CodeSigning
```

### Force Rotation
```bash
# Script automatically rotates if <90 days remaining
./scripts/setup-certificate.ps1
```

### Export Public Key (For Distribution)
```bash
az keyvault certificate download \
    --vault-name hartonomous-kv \
    --name HartIndustries-CodeSigning \
    --file HartIndustries.cer
```

**Never export the private key (.pfx) - it should remain in Key Vault only.**

## Architecture

```
???????????????????????????????????????????????
?         Azure Key Vault                     ?
?  ????????????????????????????????????????   ?
?  ?  HartIndustries-CodeSigning          ?   ?
?  ?  - Self-signed certificate           ?   ?
?  ?  - Auto-rotation (90 days)           ?   ?
?  ?  - Valid for 5 years                 ?   ?
?  ?  - Subject: CN=Hart Industries       ?   ?
?  ????????????????????????????????????????   ?
???????????????????????????????????????????????
                    ?
                    ? Managed Identity
                    ? (No passwords)
                    ?
???????????????????????????????????????????????
?      Azure DevOps Pipeline                  ?
?  1. Retrieve certificate                    ?
?  2. Install to build agent                  ?
?  3. Sign assemblies                         ?
?  4. Sign NuGet packages                     ?
?  5. Timestamp signatures                    ?
???????????????????????????????????????????????
                    ?
                    ?
???????????????????????????????????????????????
?       Signed Artifacts                      ?
?  - NuGet packages (.nupkg)                  ?
?  - Symbol packages (.snupkg)                ?
?  - Timestamped (valid after cert expires)   ?
?  - Shows "Hart Industries" as publisher     ?
???????????????????????????????????????????????
```

## Benefits

### For Developers
- ? Never manage certificates
- ? Never manage passwords
- ? Same process on all machines
- ? Works on Windows, Linux, macOS

### For Operations
- ? Automated rotation
- ? Centralized management
- ? Audit trail in Azure
- ? RBAC for access control

### For Security
- ? Zero secrets in code/pipelines
- ? Managed identities (no service principals)
- ? Private key never leaves Key Vault
- ? Automatic compliance with policies

## Troubleshooting

### Certificate Not Found in Pipeline

**Cause:** Service connection not configured or lacks permissions

**Fix:**
```bash
# Grant Key Vault access to service principal
az keyvault set-policy \
    --name hartonomous-kv \
    --spn <service-principal-id> \
    --certificate-permissions get list \
    --secret-permissions get list
```

### Certificate Expired

**Cause:** Auto-rotation failed or was disabled

**Fix:**
```bash
# Force rotation
./scripts/setup-certificate.ps1
```

### Build Agent Can't Install Certificate

**Cause:** Certificate format or permissions issue

**Fix:** Check pipeline logs - certificate should download as base64. Verify Key Vault access policy includes `get` and `list` for secrets.

## Cost

**Azure Key Vault Pricing:**
- Certificate operations: $0.03 per 10,000 operations
- Certificate renewal: $3.00 per renewal
- Estimated monthly cost: **$0.50 - $2.00**

**vs. Commercial Code Signing Certificate:**
- DigiCert/Sectigo: $400-$600/year
- Requires manual renewal
- Requires password management

**Savings: 99.5%**

