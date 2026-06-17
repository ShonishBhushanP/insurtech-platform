// ---------------------------------------------------------------------------
// sandbox-sql.bicep — minimal, budget-safe Azure SQL for the learner sandbox.
// Creates ONE Basic SQL database (≈ lowest cost) + firewall rules. No Managed
// Identity / role assignments (sandbox accounts can't write those), so the app
// connects with SQL authentication (username + password). Run the backend
// LOCALLY against this DB to demonstrate real Azure persistence.
//
//   Resources created: 1 SQL server + 1 database + up to 2 firewall rules.
//   Deploy:  az deployment group create -g <rg> -f sandbox-sql.bicep \
//              -p sqlAdminPassword=<pwd> clientIp=<your-public-ip>
// ---------------------------------------------------------------------------

@description('Resource name prefix (lowercase).')
param namePrefix string = 'insurtech'

@description('Azure region (defaults to the resource group region).')
param location string = resourceGroup().location

@description('SQL administrator login name.')
param sqlAdminLogin string = 'insurtechadmin'

@secure()
@description('SQL administrator password (12+ chars: upper, lower, digit, symbol).')
param sqlAdminPassword string

@description('Your machine public IP, to allow the local backend through the SQL firewall. Leave empty to skip.')
param clientIp string = ''

var suffix = uniqueString(resourceGroup().id)
var serverName = toLower('${namePrefix}-sql-${suffix}')
var databaseName = 'insurtech'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Single Basic-tier database (cheapest fixed tier). All three services share it —
// their table sets don't collide (Policies / Claims+OutboxEvents / Cases+Scores).
resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648 // 2 GB (Basic max)
  }
}

// Let Azure-hosted services reach the server (handy if you later run the app in Azure).
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Let your current machine reach the server (required to run the backend locally).
resource allowClient 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!empty(clientIp)) {
  parent: sqlServer
  name: 'AllowClientIp'
  properties: {
    startIpAddress: clientIp
    endIpAddress: clientIp
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = databaseName
@description('Fill in the password; use the same string for ClaimsDb / PolicyDb / FraudDb.')
output connectionStringTemplate string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};User ID=${sqlAdminLogin};Password=<PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
