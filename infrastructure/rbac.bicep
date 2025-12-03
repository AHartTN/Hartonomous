// Hartonomous Zero Trust RBAC Configuration
// Bicep for Azure Arc machines, Key Vault, App Configuration

@description('Resource Group name')
param resourceGroupName string = 'rg-hartonomous'

@description('Key Vault name')
param keyVaultName string = 'kv-hartonomous'

@description('App Configuration name')
param appConfigName string = 'appconfig-hartonomous'

@description('Hart Server Arc Machine Name')
param hartServerName string = 'hart-server'

@description('Hart Desktop Arc Machine Name')
param hartDesktopName string = 'HART-DESKTOP'

@description('API Service Principal Object ID')
param apiServicePrincipalId string

// Existing resources
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource appConfig 'Microsoft.AppConfiguration/configurationStores@2023-09-01' existing = {
  name: appConfigName
}

resource hartServer 'Microsoft.HybridCompute/machines@2024-03-31-preview' existing = {
  name: hartServerName
}

resource hartDesktop 'Microsoft.HybridCompute/machines@2024-03-31-preview' existing = {
  name: hartDesktopName
}

// Key Vault RBAC - API can read secrets
resource kvSecretsUserApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiServicePrincipalId, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: apiServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Key Vault RBAC - Arc machines can read secrets
resource kvSecretsUserHartServer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, hartServer.identity.principalId, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: hartServer.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource kvSecretsUserHartDesktop 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, hartDesktop.identity.principalId, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: hartDesktop.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// App Configuration RBAC - API can read
resource appConfigReaderApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appConfig.id, apiServicePrincipalId, 'App Configuration Data Reader')
  scope: appConfig
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '516239f1-63e1-4d78-a4de-a74fb236a071') // App Configuration Data Reader
    principalId: apiServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// App Configuration RBAC - Arc machines can read
resource appConfigReaderHartServer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appConfig.id, hartServer.identity.principalId, 'App Configuration Data Reader')
  scope: appConfig
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '516239f1-63e1-4d78-a4de-a74fb236a071')
    principalId: hartServer.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource appConfigReaderHartDesktop 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appConfig.id, hartDesktop.identity.principalId, 'App Configuration Data Reader')
  scope: appConfig
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '516239f1-63e1-4d78-a4de-a74fb236a071')
    principalId: hartDesktop.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output keyVaultId string = keyVault.id
output appConfigId string = appConfig.id
output hartServerPrincipalId string = hartServer.identity.principalId
output hartDesktopPrincipalId string = hartDesktop.identity.principalId
