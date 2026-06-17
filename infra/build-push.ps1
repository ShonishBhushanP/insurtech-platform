<#
  Builds and pushes the 7 service images to ACR using `az acr build` (no local Docker needed).
  Usage:  ./build-push.ps1 -AcrName <acrName> [-Tag latest]
#>
param(
  [Parameter(Mandatory = $true)][string]$AcrName,
  [string]$Tag = 'latest'
)
$ErrorActionPreference = 'Stop'
$backend = Join-Path (Split-Path -Parent $PSScriptRoot) 'backend'

# image name => (project path, output dll)
$images = @(
  @{ img = 'policy';    proj = 'src/Services/Policy/Policy.Api/Policy.Api.csproj';          dll = 'Policy.Api.dll' },
  @{ img = 'claims';    proj = 'src/Services/Claims/Claims.Api/Claims.Api.csproj';          dll = 'Claims.Api.dll' },
  @{ img = 'fraud';     proj = 'src/Services/Fraud/Fraud.Api/Fraud.Api.csproj';             dll = 'Fraud.Api.dll' },
  @{ img = 'documents'; proj = 'src/Services/Documents/DocMgmt.Api/DocMgmt.Api.csproj';     dll = 'DocMgmt.Api.dll' },
  @{ img = 'payments';  proj = 'src/Services/Payments/Payments.Api/Payments.Api.csproj';    dll = 'Payments.Api.dll' },
  @{ img = 'partner';   proj = 'src/Services/Partner/Partner.Api/Partner.Api.csproj';       dll = 'Partner.Api.dll' },
  @{ img = 'gateway';   proj = 'src/Gateway/InsurTech.Gateway/InsurTech.Gateway.csproj';    dll = 'InsurTech.Gateway.dll' }
)

foreach ($i in $images) {
  Write-Host "==> Building $($i.img):$Tag"
  az acr build `
    --registry $AcrName `
    --image "$($i.img):$Tag" `
    --build-arg "PROJECT=$($i.proj)" `
    --build-arg "DLL=$($i.dll)" `
    --file Dockerfile `
    $backend
}
Write-Host "All images pushed to $AcrName."
