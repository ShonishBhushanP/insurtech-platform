<#
  End-to-end deploy of the InsurTech platform to Azure.
  Prereqs: Azure CLI (`az login` done), an existing or new resource group, Contributor + User
  Access Administrator (the platform creates role assignments).

  Steps:
    1. Provision the platform (data tier + shared platform + ACR + Container Apps env + RBAC).
    2. Build & push the 7 service images to ACR.
    3. Deploy the Container Apps wired to the platform.
    4. Print the public gateway URL.

  Usage:
    ./deploy.ps1 -ResourceGroup rg-insurtech -Location centralindia
#>
param(
  [Parameter(Mandatory = $true)][string]$ResourceGroup,
  [string]$Location = 'centralindia',
  [string]$NamePrefix = 'insurtech',
  [string]$Tag = 'latest'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

Write-Host "==> [1/4] Resource group $ResourceGroup ($Location)"
az group create --name $ResourceGroup --location $Location --output none

Write-Host "==> [1/4] Deploying platform.bicep (this provisions SQL, Cosmos, Storage, Redis, Service Bus, Key Vault, ACR, Container Apps env)"
$platform = az deployment group create `
  --resource-group $ResourceGroup `
  --template-file (Join-Path $here 'platform.bicep') `
  --parameters (Join-Path $here 'platform.bicepparam') `
  --parameters namePrefix=$NamePrefix `
  --query properties.outputs -o json | ConvertFrom-Json

$acrLoginServer = $platform.acrLoginServer.value
$acrName = $acrLoginServer.Split('.')[0]

Write-Host "==> [2/4] Building & pushing images to $acrName"
& (Join-Path $here 'build-push.ps1') -AcrName $acrName -Tag $Tag

Write-Host "==> [3/4] Deploying apps.bicep (the 6 microservices + gateway)"
$apps = az deployment group create `
  --resource-group $ResourceGroup `
  --template-file (Join-Path $here 'apps.bicep') `
  --parameters `
    namePrefix=$NamePrefix `
    imageTag=$Tag `
    identityResourceId=$($platform.identityResourceId.value) `
    identityClientId=$($platform.identityClientId.value) `
    acrLoginServer=$($platform.acrLoginServer.value) `
    caeId=$($platform.caeId.value) `
    caeDefaultDomain=$($platform.caeDefaultDomain.value) `
    appInsightsConnectionString=$($platform.appInsightsConnectionString.value) `
    keyVaultUri=$($platform.keyVaultUri.value) `
    serviceBusFqdn=$($platform.serviceBusFqdn.value) `
    storageBlobEndpoint=$($platform.storageBlobEndpoint.value) `
    cosmosEndpoint=$($platform.cosmosEndpoint.value) `
    sqlServerFqdn=$($platform.sqlServerFqdn.value) `
    redisHostName=$($platform.redisHostName.value) `
    redisName=$($platform.redisName.value) `
  --query properties.outputs -o json | ConvertFrom-Json

Write-Host ""
Write-Host "==> [4/4] Done."
Write-Host "Gateway URL : $($apps.gatewayUrl.value)"
Write-Host ""
Write-Host "IMPORTANT post-step — grant the app identity SQL access (AAD). Connect to each DB as the"
Write-Host "Entra admin and run (replace <prefix>-id with the managed identity name):"
Write-Host "  CREATE USER [<prefix>-id] FROM EXTERNAL PROVIDER;"
Write-Host "  ALTER ROLE db_datareader ADD MEMBER [<prefix>-id];"
Write-Host "  ALTER ROLE db_datawriter ADD MEMBER [<prefix>-id];"
Write-Host "  ALTER ROLE db_ddladmin   ADD MEMBER [<prefix>-id];   -- EnsureCreated needs DDL"
