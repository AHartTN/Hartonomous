# Development Token Guide for Hartonomous API

## Summary of Changes

### 1. Configuration Fix
**File**: `Hartonomous.Api/appsettings.Development.json`
- ✅ Renamed `"Authentication"` section to `"AzureAd"`
- ✅ Added missing fields: `Domain`, `CallbackPath`
- ✅ Configuration now matches what `AuthenticationConfiguration.cs` expects

### 2. Dev Token Endpoint
**File**: `Hartonomous.API/Controllers/DevTokenController.cs`
- ✅ Created `/api/v1/DevToken/token` endpoint for generating JWT tokens
- ✅ Development-only (disabled in production)
- ✅ Generates tokens with configurable roles and scopes
- ✅ Includes sample token configurations

### 3. Authentication Configuration Enhancement
**File**: `Hartonomous.Infrastructure/Security/AuthenticationConfiguration.cs`
- ✅ Added support for dev tokens signed with symmetric keys
- ✅ Configured audience validation to accept dev tokens
- ✅ Lenient validation for development (no issuer validation)
- ✅ Still supports real Azure AD tokens

## Azure AD Configuration Status

### ✅ Correctly Configured
- **App Registration**: `c25ed11d-c712-4574-8897-6a3a0c8dbb7f` exists
- **Tenant**: `6c9c44c4-f04b-4b5f-bea0-f1069179799c`
- **API Scopes**: All required scopes are configured
  - `api.access`
  - `api.admin`
  - `api.read`
  - `api.write`
  - `access_as_user`
- **Admin Consent**: ✅ Granted for all scopes
- **App Roles**: `Admin`, `User`, `DataScientist`
- **User Assignment**: Anthony Hart → Admin role

## How to Use Dev Tokens

### Option 1: Get Token via API

**1. Get available sample configurations:**
```bash
curl -k https://localhost:7001/api/v1/DevToken/samples
```

**2. Generate an Admin token:**
```bash
curl -k -X POST https://localhost:7001/api/v1/DevToken/token \
  -H "Content-Type: application/json" \
  -d '{
    "roles": ["Admin", "User"],
    "scopes": ["api.access", "api.admin", "api.read", "api.write", "access_as_user"],
    "userId": "test-user-123",
    "email": "test@hartonomous.local",
    "name": "Test Admin",
    "expirationHours": 8
  }'
```

**3. Use the token:**
```bash
TOKEN="<your-token-here>"
curl -k -X GET "https://localhost:7001/api/v1/System/gpu-status" \
  -H "Authorization: Bearer $TOKEN"
```

### Option 2: Test with Different User Types

**Admin User** (full access):
```json
{
  "roles": ["Admin", "User"],
  "scopes": ["api.access", "api.admin", "api.read", "api.write", "access_as_user"]
}
```

**Standard User** (read-only):
```json
{
  "roles": ["User"],
  "scopes": ["api.read", "access_as_user"]
}
```

**Data Scientist** (read/write):
```json
{
  "roles": ["DataScientist", "User"],
  "scopes": ["api.read", "api.write", "access_as_user"]
}
```

## Swagger/OpenAPI Usage

When the API is running in Development mode:

1. Navigate to: `https://localhost:7001/api-docs`
2. Generate a dev token using the endpoint or curl
3. Click the **Authorize** button in Swagger UI
4. Enter: `Bearer <your-token>` (include the word "Bearer")
5. Click **Authorize**
6. Test any endpoint with authentication

## PowerShell Helper Script

```powershell
# Get a dev token and save it
$response = Invoke-RestMethod -Uri "https://localhost:7001/api/v1/DevToken/token" `
    -Method POST `
    -ContentType "application/json" `
    -Body '{"roles":["Admin","User"],"scopes":["api.access","api.admin","api.read","api.write","access_as_user"]}' `
    -SkipCertificateCheck

$token = $response.token
Write-Host "Token: $token"

# Use the token
$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
}

Invoke-RestMethod -Uri "https://localhost:7001/api/v1/System/gpu-status" `
    -Headers $headers `
    -SkipCertificateCheck
```

## How Dev Tokens Work

1. **Token Generation**:
   - Uses HMAC-SHA256 symmetric signing (dev-only secret key)
   - Includes all standard JWT claims (sub, email, name, roles, scopes)
   - Matches Azure AD token structure

2. **Token Validation**:
   - In Development: Accepts both dev tokens (symmetric key) and Azure AD tokens
   - In Production: Only accepts Azure AD tokens (disabled dev token validation)
   - Validates: signature, audience, lifetime
   - Skips: issuer validation (dev mode only)

3. **Security**:
   - Dev token endpoint only available in Development environment
   - Returns 403 Forbidden in production
   - Uses `#if !DEBUG` to hide from production API documentation

## Testing Real Azure AD Tokens

To test with real Azure AD tokens (for production validation):

1. Use Azure AD login flow in your client application
2. Request scopes: `api://c25ed11d-c712-4574-8897-6a3a0c8dbb7f/.default`
3. The API will validate using Azure AD public keys
4. Works the same way dev tokens do, just more secure

## Troubleshooting

### Token returns 401 Unauthorized
- Check that token hasn't expired (default: 8 hours)
- Verify `Authorization` header format: `Bearer <token>`
- Check API logs for specific validation errors

### Endpoint returns 403 Forbidden
- User lacks required role or scope
- Check authorization policy requirements in the controller
- Verify token has correct claims: `roles` and `scope`

### Dev token endpoint returns 403
- Only works in Development environment
- Check `ASPNETCORE_ENVIRONMENT=Development`

## Files Modified

1. `Hartonomous.Api/appsettings.Development.json` - Fixed AzureAd configuration
2. `Hartonomous.API/Controllers/DevTokenController.cs` - New dev token endpoint
3. `Hartonomous.Infrastructure/Security/AuthenticationConfiguration.cs` - Dev token support
4. `Hartonomous.API/Configuration/SwaggerConfiguration.cs` - Allow anonymous access to OpenAPI endpoint

## Next Steps

- ✅ Configuration fixed
- ✅ Dev token endpoint working
- ✅ Authentication validated
- ✅ Ready for API development and testing!

## Notes

- Dev tokens are **only for local development testing**
- Production deployments will use real Azure AD tokens
- The dev token secret key is hardcoded for development convenience
- All admin-required scopes have proper admin consent in Azure AD
