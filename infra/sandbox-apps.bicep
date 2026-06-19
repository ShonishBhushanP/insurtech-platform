// ---------------------------------------------------------------------------
// sandbox-apps.bicep — backend on Azure Container Apps WITHOUT Managed Identity/RBAC
// (sandbox accounts can't write role assignments). Image pull uses ACR admin creds;
// SQL uses a connection string. 9 internal microservices + a public gateway.
// Container Apps is the managed stand-in for the diagram's AKS.
// ---------------------------------------------------------------------------

param location string = resourceGroup().location
param namePrefix string = 'insurtech'
param acrLoginServer string
param acrUsername string
@secure()
param acrPassword string
param imageTag string = 'latest'
@secure()
param sqlConnectionString string
@description('Static Web App / frontend origin allowed by the gateway CORS. Optional.')
param frontendOrigin string = ''

var internalServices = [
  'policy', 'claims', 'fraud', 'documents', 'payments', 'partner', 'underwriting', 'notification', 'audit'
]

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-law'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-cae'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
  }
}

var domain = env.properties.defaultDomain

// Common container config shared by every app.
var commonSecrets = [
  { name: 'acr-pwd', value: acrPassword }
  { name: 'sql-cs', value: sqlConnectionString }
]
var commonEnv = [
  { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
  { name: 'Kestrel__Endpoints__Http__Url', value: 'http://+:8080' }
]

// Per-service extra env (SQL connection strings + Claims downstream URLs).
// (Bicep user-defined functions can't see outer params, so prefix is passed in.)
func extraEnv(s string, prefix string, dom string) array =>
  s == 'claims' ? [
    { name: 'ConnectionStrings__ClaimsDb', secretRef: 'sql-cs' }
    { name: 'Claims__Services__Fraud', value: 'https://${prefix}-fraud.internal.${dom}' }
    { name: 'Claims__Services__Payments', value: 'https://${prefix}-payments.internal.${dom}' }
  ] : s == 'policy' ? [
    { name: 'ConnectionStrings__PolicyDb', secretRef: 'sql-cs' }
  ] : s == 'fraud' ? [
    { name: 'ConnectionStrings__FraudDb', secretRef: 'sql-cs' }
  ] : []

resource services 'Microsoft.App/containerApps@2024-03-01' = [for s in internalServices: {
  name: '${namePrefix}-${s}'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: [ { server: acrLoginServer, username: acrUsername, passwordSecretRef: 'acr-pwd' } ]
      secrets: commonSecrets
    }
    template: {
      containers: [ {
        name: s
        image: '${acrLoginServer}/${namePrefix}-${s}:${imageTag}'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: concat(commonEnv, extraEnv(s, namePrefix, domain))
      } ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}]

// Public gateway (YARP) — overrides each cluster destination to the internal service FQDNs.
resource gateway 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-gateway'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [ { server: acrLoginServer, username: acrUsername, passwordSecretRef: 'acr-pwd' } ]
      secrets: [ { name: 'acr-pwd', value: acrPassword } ]
    }
    template: {
      containers: [ {
        name: 'gateway'
        image: '${acrLoginServer}/${namePrefix}-gateway:${imageTag}'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: concat(commonEnv, [
          { name: 'ReverseProxy__Clusters__policy__Destinations__d1__Address', value: 'https://${namePrefix}-policy.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__claims__Destinations__d1__Address', value: 'https://${namePrefix}-claims.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__fraud__Destinations__d1__Address', value: 'https://${namePrefix}-fraud.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__documents__Destinations__d1__Address', value: 'https://${namePrefix}-documents.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__payments__Destinations__d1__Address', value: 'https://${namePrefix}-payments.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__partner__Destinations__d1__Address', value: 'https://${namePrefix}-partner.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__underwriting__Destinations__d1__Address', value: 'https://${namePrefix}-underwriting.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__notification__Destinations__d1__Address', value: 'https://${namePrefix}-notification.internal.${domain}' }
          { name: 'ReverseProxy__Clusters__audit__Destinations__d1__Address', value: 'https://${namePrefix}-audit.internal.${domain}' }
          { name: 'Cors__Origins', value: frontendOrigin }
        ])
      } ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}

output gatewayUrl string = 'https://${gateway.properties.configuration.ingress.fqdn}'
