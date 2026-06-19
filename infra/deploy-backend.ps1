<#
  Stage 2 - deploy the backend (9 microservices + gateway) to Azure Container Apps on a
  sandbox subscription (Contributor only; no Managed Identity / RBAC).

  Steps: create ACR (admin) -> az acr build each image server-side -> deploy sandbox-apps.bicep.
  Run right after 'az login' (set the proxy cert var first).

      $env:REQUESTS_CA_BUNDLE = "$env:USERPROFILE\az-cacert.pem"
      az login --use-device-code
      ./deploy-backend.ps1 -ResourceGroup rg-azuser7069_mml.local-yyRMB `
          -SqlConnectionString "Server=tcp:...;Database=insurtech;User ID=insurtechadmin;Password=...;Encrypt=True;" `
          -FrontendOrigin "https://ituia7ad58ee.z29.web.core.windows.net"

  NOTE: building 10 images server-side takes ~15-25 min. Sandbox tokens expire ~15 min, so this
  may need a re-run (it is idempotent). On an Owner subscription the full infra/deploy.ps1 (with
  Managed Identity) is the production path; this is the no-RBAC sandbox variant.
#>
param(
  [Parameter(Mandatory = $true)] [string]$ResourceGroup,
  [Parameter(Mandatory = $true)] [string]$SqlConnectionString,
  [string]$Location = "centralindia",
  [string]$NamePrefix = "insurtech",
  [string]$FrontendOrigin = "",
  [string]$ImageTag = "latest"
)
$ErrorActionPreference = "Continue"
$az = (Get-Command az -ErrorAction SilentlyContinue).Source
if (-not $az) { $az = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd" }
# Corporate-proxy CA bundle (Windows) — only set when present, so this also runs in Azure Cloud Shell (Linux).
if ($env:USERPROFILE) {
  $caBundle = Join-Path $env:USERPROFILE 'az-cacert.pem'
  if (Test-Path $caBundle) { $env:REQUESTS_CA_BUNDLE = $caBundle }
}

$repo = Split-Path -Parent $PSScriptRoot
$ctx = Join-Path $repo "backend"   # Docker build context

# Service -> (project path, output DLL). Names become image repos: insurtech-<name>.
$svc = [ordered]@{
  policy       = @{ proj = "src/Services/Policy/Policy.Api/Policy.Api.csproj";            dll = "Policy.Api.dll" }
  claims       = @{ proj = "src/Services/Claims/Claims.Api/Claims.Api.csproj";            dll = "Claims.Api.dll" }
  fraud        = @{ proj = "src/Services/Fraud/Fraud.Api/Fraud.Api.csproj";               dll = "Fraud.Api.dll" }
  documents    = @{ proj = "src/Services/Documents/DocMgmt.Api/DocMgmt.Api.csproj";       dll = "DocMgmt.Api.dll" }
  payments     = @{ proj = "src/Services/Payments/Payments.Api/Payments.Api.csproj";      dll = "Payments.Api.dll" }
  partner      = @{ proj = "src/Services/Partner/Partner.Api/Partner.Api.csproj";         dll = "Partner.Api.dll" }
  underwriting = @{ proj = "src/Services/Underwriting/Underwriting.Api/Underwriting.Api.csproj"; dll = "Underwriting.Api.dll" }
  notification = @{ proj = "src/Services/Notification/Notification.Api/Notification.Api.csproj"; dll = "Notification.Api.dll" }
  audit        = @{ proj = "src/Services/Audit/Audit.Api/Audit.Api.csproj";               dll = "Audit.Api.dll" }
  gateway      = @{ proj = "src/Gateway/InsurTech.Gateway/InsurTech.Gateway.csproj";      dll = "InsurTech.Gateway.dll" }
}

# 1. ACR (admin enabled for credential-based pull — no role assignment needed).
#    Deterministic, RG-derived name (SHA-256 — stable across machines/PowerShell versions, unlike GetHashCode).
$sha = [System.Security.Cryptography.SHA256]::Create()
$rgHash = [System.BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($ResourceGroup))).Replace('-', '').ToLower().Substring(0, 8)
$acr = "$($NamePrefix)acr$rgHash"
Write-Host "==> ACR $acr"
& $az acr create -n $acr -g $ResourceGroup --sku Basic --admin-enabled true -o none
if ($LASTEXITCODE -ne 0) { Write-Host "ACR create failed (token? run az login)"; exit 1 }
$loginServer = & $az acr show -n $acr -g $ResourceGroup --query loginServer -o tsv

# 2. Build each image server-side in ACR (no local Docker needed).
#    Resumable: skip images already in ACR so a re-run (after a token expiry) continues.
foreach ($name in $svc.Keys) {
  $already = & $az acr repository show -n $acr --image "$NamePrefix-$($name):$ImageTag" --query name -o tsv 2>$null
  if ($already) { Write-Host "==> skip insurtech-$name (already in ACR)"; continue }
  Write-Host "==> az acr build insurtech-$name"
  & $az acr build -r $acr -t "$NamePrefix-$($name):$ImageTag" `
    --build-arg "PROJECT=$($svc[$name].proj)" --build-arg "DLL=$($svc[$name].dll)" `
    -f (Join-Path $ctx "Dockerfile") $ctx -o none
  if ($LASTEXITCODE -ne 0) { Write-Host "build failed for $name (re-run script to resume)"; exit 1 }
}

# 3. ACR credentials for the Container Apps pull
$acrUser = & $az acr credential show -n $acr --query username -o tsv
$acrPwd = & $az acr credential show -n $acr --query "passwords[0].value" -o tsv

# 4. Deploy the apps
Write-Host "==> Deploying Container Apps"
$out = & $az deployment group create -g $ResourceGroup -f (Join-Path $PSScriptRoot "sandbox-apps.bicep") `
  -p namePrefix=$NamePrefix acrLoginServer=$loginServer acrUsername=$acrUser acrPassword=$acrPwd `
     imageTag=$ImageTag sqlConnectionString=$SqlConnectionString frontendOrigin=$FrontendOrigin `
  --query "properties.outputs.gatewayUrl.value" -o tsv
if ($LASTEXITCODE -ne 0) { Write-Host "apps deploy failed"; exit 1 }

Write-Host ""
Write-Host "============================================================"
Write-Host " Backend gateway (public): $out"
Write-Host " Repoint the frontend: set VITE_API_BASE=$out in the SWA workflow and push."
Write-Host "============================================================"
