// ─────────────────────────────────────────────────────────────────────────────
// InsurTech — Platform infrastructure (resource-group scope).
// Provisions the Data Tier + Shared Platform from the Azure deployment diagram:
//   SQL · Cosmos · Blob · Redis · Service Bus · Key Vault · Monitor + a Container Apps
//   environment (managed stand-in for AKS) + ACR + a shared user-assigned identity with RBAC.
// Container apps themselves are deployed by apps.bicep AFTER images are pushed (see deploy.ps1).
// ─────────────────────────────────────────────────────────────────────────────

@description('Primary region (deployment diagram: Central India).')
param location string = resourceGroup().location

@description('Short prefix for resource names (3-10 lowercase alphanumerics).')
@minLength(3)
@maxLength(10)
param namePrefix string = 'insurtech'

@description('Object ID (Entra) of the user/group to set as the SQL Entra admin.')
param sqlAadAdminObjectId string

@description('Display name/UPN of the SQL Entra admin.')
param sqlAadAdminLogin string

var suffix = uniqueString(resourceGroup().id)
var tags = { platform: 'InsurTech', phase: 'Phase3', managedBy: 'bicep' }

// ── Shared user-assigned managed identity (Workload Identity for the apps — LLD IR-06) ──
resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id'
  location: location
  tags: tags
}

// ── Observability: Log Analytics + Application Insights (Shared Platform — Monitor) ──
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-law'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-ai'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ── Key Vault (HSM-capable; RBAC authorization) — LLD IR-04 ──
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${namePrefix}kv${take(suffix, 6)}'
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    publicNetworkAccess: 'Enabled'
  }
}

// ── Azure SQL (Data Tier — transactional; SQL MI in production) ──
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${namePrefix}-sql-${take(suffix, 6)}'
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAadAdminLogin
      sid: sqlAadAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

var databases = [ 'PolicyDb', 'ClaimsDb', 'FraudDb' ]
resource sqlDbs 'Microsoft.Sql/servers/databases@2023-08-01-preview' = [for db in databases: {
  parent: sqlServer
  name: db
  location: location
  tags: tags
  sku: { name: 'S0', tier: 'Standard' }
}]

// ── Cosmos DB (Data Tier — read models + document metadata) ──
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: '${namePrefix}-cosmos-${take(suffix, 6)}'
  location: location
  kind: 'GlobalDocumentDB'
  tags: tags
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [ { name: 'EnableServerless' } ]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [ { locationName: location, failoverPriority: 0 } ]
    disableLocalAuth: true // AAD-only data plane (LLD — no keys)
  }
}

resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: 'insurtech'
  properties: { resource: { id: 'insurtech' } }
}

var cosmosContainers = [
  { name: 'docs-metadata', pk: '/ownerPolicyId' }
  { name: 'claims-read-model', pk: '/policyId' }
  { name: 'fraud-cases-read-model', pk: '/policyId' }
]
resource cosmosColls 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = [for c in cosmosContainers: {
  parent: cosmosDb
  name: c.name
  properties: {
    resource: {
      id: c.name
      partitionKey: { paths: [ c.pk ], kind: 'Hash' }
    }
  }
}]

// ── Storage (Data Tier — Blob: staging / immutable / archive) ──
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: '${namePrefix}st${take(suffix, 8)}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

var blobContainers = [ 'docs-staging', 'docs-immutable', 'docs-archive' ]
resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for name in blobContainers: {
  parent: blobService
  name: name
}]

// ── Azure Cache for Redis (Data Tier — cache + idempotency) ──
resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${namePrefix}-redis-${take(suffix, 6)}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'Basic', family: 'C', capacity: 0 }
    minimumTlsVersion: '1.2'
  }
}

// ── Service Bus (Shared Platform — commands + events) ──
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${namePrefix}-sb-${take(suffix, 6)}'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' }
}

var topics = [ 'claims-events', 'fraud-events' ]
resource sbTopics 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = [for t in topics: {
  parent: serviceBus
  name: t
  properties: { enablePartitioning: false }
}]

// ── Azure Container Registry ──
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${namePrefix}acr${take(suffix, 8)}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

// ── Container Apps environment (managed stand-in for AKS — Primary Region compute) ──
resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ─────────────────── RBAC: grant the shared identity data-plane access ───────────────────
var kvSecretsUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var sbDataOwner   = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419')
var blobDataContrib = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
var acrPull = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource raKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, uami.id, kvSecretsUser)
  properties: { roleDefinitionId: kvSecretsUser, principalId: uami.properties.principalId, principalType: 'ServicePrincipal' }
}
resource raSb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: serviceBus
  name: guid(serviceBus.id, uami.id, sbDataOwner)
  properties: { roleDefinitionId: sbDataOwner, principalId: uami.properties.principalId, principalType: 'ServicePrincipal' }
}
resource raBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, uami.id, blobDataContrib)
  properties: { roleDefinitionId: blobDataContrib, principalId: uami.properties.principalId, principalType: 'ServicePrincipal' }
}
resource raAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, uami.id, acrPull)
  properties: { roleDefinitionId: acrPull, principalId: uami.properties.principalId, principalType: 'ServicePrincipal' }
}

// Cosmos data-plane (built-in Data Contributor) — assigned to the identity.
resource cosmosDataContrib 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, uami.id, 'cosmos-data-contributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: uami.properties.principalId
    scope: cosmos.id
  }
}

// ─────────────────── Outputs consumed by apps.bicep ───────────────────
output identityResourceId string = uami.id
output identityClientId string = uami.properties.clientId
output acrLoginServer string = acr.properties.loginServer
output caeId string = caEnv.id
output caeDefaultDomain string = caEnv.properties.defaultDomain
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output keyVaultUri string = keyVault.properties.vaultUri
output serviceBusFqdn string = '${serviceBus.name}.servicebus.windows.net'
output storageBlobEndpoint string = storage.properties.primaryEndpoints.blob
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output redisHostName string = redis.properties.hostName
output redisName string = redis.name
