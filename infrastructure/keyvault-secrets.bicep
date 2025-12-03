// Store PostgreSQL connection strings in Key Vault
// Secrets for localhost/dev/staging/production

@description('Key Vault name')
param keyVaultName string = 'kv-hartonomous'

@secure()
@description('PostgreSQL localhost connection string')
param postgresLocalhostConnection string

@secure()
@description('PostgreSQL dev connection string')
param postgresDevConnection string

@secure()
@description('PostgreSQL staging connection string')
param postgresStagingConnection string

@secure()
@description('PostgreSQL production connection string')
param postgresProductionConnection string

@secure()
@description('Entra ID Tenant ID')
param tenantId string

@secure()
@description('API Client ID')
param apiClientId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource secretPostgresLocalhost 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection-localhost'
  properties: {
    value: postgresLocalhostConnection
    contentType: 'text/plain'
  }
}

resource secretPostgresDev 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection-dev'
  properties: {
    value: postgresDevConnection
    contentType: 'text/plain'
  }
}

resource secretPostgresStaging 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection-staging'
  properties: {
    value: postgresStagingConnection
    contentType: 'text/plain'
  }
}

resource secretPostgresProduction 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection-production'
  properties: {
    value: postgresProductionConnection
    contentType: 'text/plain'
  }
}

resource secretTenantId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureAd--TenantId'
  properties: {
    value: tenantId
    contentType: 'text/plain'
  }
}

resource secretClientId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureAd--ClientId'
  properties: {
    value: apiClientId
    contentType: 'text/plain'
  }
}

output keyVaultUri string = keyVault.properties.vaultUri
