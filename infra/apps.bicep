// ─────────────────────────────────────────────────────────────────────────────
// InsurTech — Container Apps (the 6 microservices + YARP gateway).
// Deploy AFTER platform.bicep and AFTER images are pushed to ACR (see deploy.ps1).
// Each app runs under the shared user-assigned identity; AZURE_CLIENT_ID drives
// DefaultAzureCredential so SQL/Service Bus/Blob/Cosmos/Key Vault all authenticate with
// no secrets. The app's config switches (Azure:* / ConnectionStrings:*) activate the
// Azure adapters built into the services.
// ─────────────────────────────────────────────────────────────────────────────

param location string = resourceGroup().location
param namePrefix string = 'insurtech'
param imageTag string = 'latest'

param identityResourceId string
param identityClientId string
param acrLoginServer string
param caeId string
param caeDefaultDomain string
param appInsightsConnectionString string
param keyVaultUri string
param serviceBusFqdn string
param storageBlobEndpoint string
param cosmosEndpoint string
param sqlServerFqdn string
param redisHostName string
param redisName string

var tags = { platform: 'InsurTech', phase: 'Phase3', managedBy: 'bicep' }

resource redis 'Microsoft.Cache/redis@2024-03-01' existing = { name: redisName }
var redisConn = '${redisHostName}:6380,password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'

// Internal service URLs within the Container Apps environment.
var policyUrl    = 'https://${namePrefix}-policy.internal.${caeDefaultDomain}'
var claimsUrl    = 'https://${namePrefix}-claims.internal.${caeDefaultDomain}'
var fraudUrl     = 'https://${namePrefix}-fraud.internal.${caeDefaultDomain}'
var documentsUrl = 'https://${namePrefix}-documents.internal.${caeDefaultDomain}'
var paymentsUrl  = 'https://${namePrefix}-payments.internal.${caeDefaultDomain}'
var partnerUrl   = 'https://${namePrefix}-partner.internal.${caeDefaultDomain}'

var commonEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'AZURE_CLIENT_ID', value: identityClientId }
  { name: 'Kestrel__Endpoints__Http__Url', value: 'http://+:8080' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
  { name: 'Azure__KeyVault__Uri', value: keyVaultUri }
  { name: 'Azure__ServiceBus__FullyQualifiedNamespace', value: serviceBusFqdn }
  { name: 'Azure__Redis__ConnectionString', secretRef: 'redis-conn' }
]

var sqlBase = 'Server=tcp:${sqlServerFqdn},1433;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
var policyConn = replace(sqlBase, 'Server=tcp:', 'Database=PolicyDb;Server=tcp:')
var claimsConn = replace(sqlBase, 'Server=tcp:', 'Database=ClaimsDb;Server=tcp:')
var fraudConn  = replace(sqlBase, 'Server=tcp:', 'Database=FraudDb;Server=tcp:')

var cpu = json('0.5')
var memory = '1Gi'

resource policy 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-policy'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: [ { server: acrLoginServer, identity: identityResourceId } ]
      secrets: [ { name: 'redis-conn', value: redisConn } ]
    }
    template: {
      containers: [ {
        name: 'app'
        image: '${acrLoginServer}/policy:${imageTag}'
        resources: { cpu: cpu, memory: memory }
        env: concat(commonEnv, [ { name: 'ConnectionStrings__PolicyDb', value: policyConn } ])
      } ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource fraud 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-fraud'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: [ { server: acrLoginServer, identity: identityResourceId } ]
      secrets: [ { name: 'redis-conn', value: redisConn } ]
    }
    template: {
      containers: [ {
        name: 'app'
        image: '${acrLoginServer}/fraud:${imageTag}'
        resources: { cpu: cpu, memory: memory }
        env: concat(commonEnv, [ { name: 'ConnectionStrings__FraudDb', value: fraudConn } ])
      } ]
      scale: { minReplicas: 1, maxReplicas: 5 }
    }
  }
}

resource documents 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-documents'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: [ { server: acrLoginServer, identity: identityResourceId } ]
      secrets: [ { name: 'redis-conn', value: redisConn } ]
    }
    template: {
      containers: [ {
        name: 'app'
        image: '${acrLoginServer}/documents:${imageTag}'
        resources: { cpu: cpu, memory: memory }
        env: concat(commonEnv, [
          { name: 'Azure__Storage__BlobServiceUri', value: storageBlobEndpoint }
          { name: 'Azure__Cosmos__Endpoint', value: cosmosEndpoint }
          { name: 'Azure__Cosmos__Database', value: 'insurtech' }
        ])
      } ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource payments 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-payments'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: [ { server: acrLoginServer, identity: identityResourceId } ]
      secrets: [ { name: 'redis-conn', value: redisConn } ]
    }
    template: {
      containers: [ {
        name: 'app'
        image: '${acrLoginServer}/payments:${imageTag}'
        resources: { cpu: cpu, memory: memory }
        env: commonEnv
      } ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource partner 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-partner'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: [ { server: acrLoginServer, identity: identityResourceId } ]
      secrets: [ { name: 'redis-conn', value: redisConn } ]
    }
    template: {
      containers: [ {
        name: 'app'
        image: '${acrLoginServer}/partner:${imageTag}'
        resources: { cpu: cpu, memory: memory }
        env: commonEnv
      } ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource claims 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-claims'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: [ { server: acrLoginServer, identity: identityResourceId } ]
      secrets: [ { name: 'redis-conn', value: redisConn } ]
    }
    template: {
      containers: [ {
        name: 'app'
        image: '${acrLoginServer}/claims:${imageTag}'
        resources: { cpu: cpu, memory: memory }
        env: concat(commonEnv, [
          { name: 'ConnectionStrings__ClaimsDb', value: claimsConn }
          { name: 'Claims__Services__Fraud', value: fraudUrl }
          { name: 'Claims__Services__Payments', value: paymentsUrl }
        ])
      } ]
      scale: { minReplicas: 1, maxReplicas: 5 }
    }
  }
}

// API Gateway (YARP) — public ingress; reverse-proxies to the internal services.
resource gateway 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-gateway'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto' }
      registries: [ { server: acrLoginServer, identity: identityResourceId } ]
    }
    template: {
      containers: [ {
        name: 'app'
        image: '${acrLoginServer}/gateway:${imageTag}'
        resources: { cpu: cpu, memory: memory }
        env: [
          { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
          { name: 'Kestrel__Endpoints__Http__Url', value: 'http://+:8080' }
          { name: 'ReverseProxy__Clusters__policy__Destinations__d1__Address', value: policyUrl }
          { name: 'ReverseProxy__Clusters__claims__Destinations__d1__Address', value: claimsUrl }
          { name: 'ReverseProxy__Clusters__fraud__Destinations__d1__Address', value: fraudUrl }
          { name: 'ReverseProxy__Clusters__documents__Destinations__d1__Address', value: documentsUrl }
          { name: 'ReverseProxy__Clusters__payments__Destinations__d1__Address', value: paymentsUrl }
          { name: 'ReverseProxy__Clusters__partner__Destinations__d1__Address', value: partnerUrl }
        ]
      } ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

output gatewayFqdn string = gateway.properties.configuration.ingress.fqdn
output gatewayUrl string = 'https://${gateway.properties.configuration.ingress.fqdn}'
