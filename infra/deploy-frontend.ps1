<#
  Stage 3 - deploy the React UI to an Azure Storage static website.
  Needs only Azure CLI + Contributor on the resource group (no RBAC, no native tools).

  Run it in ONE go right after logging in (sandbox tokens expire fast):
      az login --use-device-code
      ./deploy-frontend.ps1 -ResourceGroup rg-azuser7069_mml.local-yyRMB

  Re-runnable: the storage account name is derived from the RG, so a second run just
  re-uploads the latest build to the same site.
#>
param(
  [Parameter(Mandatory = $true)] [string]$ResourceGroup,
  [string]$Location = "centralindia",
  [string]$ApiBase = "http://localhost:8080"
)
# Keep going on benign native stderr (az/npm warn on stderr); we check $LASTEXITCODE explicitly.
$ErrorActionPreference = "Continue"

$az = (Get-Command az -ErrorAction SilentlyContinue).Source
if (-not $az) { $az = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd" }

$repo = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $repo "frontend"

# 1. Build the frontend with the chosen API base (VITE_ env var overrides .env)
Write-Host "==> Building frontend (VITE_API_BASE=$ApiBase)"
Push-Location $frontend
$env:VITE_API_BASE = $ApiBase
npm run build
$buildOk = ($LASTEXITCODE -eq 0)
Pop-Location
if (-not $buildOk) { Write-Host "Frontend build failed."; exit 1 }

# 2. Deterministic, RG-scoped storage account name (3-24 lowercase alphanumerics)
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = [System.BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($ResourceGroup))).Replace('-', '').ToLower().Substring(0, 8)
$sa = "itui$hash"
Write-Host "==> Storage account: $sa"

# 3. Create the account (idempotent) and enable static website
& $az storage account create -n $sa -g $ResourceGroup -l $Location --sku Standard_LRS --kind StorageV2 -o none
if ($LASTEXITCODE -ne 0) { Write-Host "Storage create failed. If this is an access-pass error, run az login --use-device-code and re-run."; exit 1 }

$key = (& $az storage account keys list -g $ResourceGroup -n $sa --query "[0].value" -o tsv)
& $az storage blob service-properties update --account-name $sa --account-key $key --static-website --index-document index.html --404-document index.html -o none

# 4. Upload the built site to the web container
$dist = Join-Path $frontend "dist"
& $az storage blob upload-batch -s $dist -d '$web' --account-name $sa --account-key $key --overwrite -o none
if ($LASTEXITCODE -ne 0) { Write-Host "Upload failed."; exit 1 }

# 5. Print the public URL
$url = (& $az storage account show -n $sa -g $ResourceGroup --query "primaryEndpoints.web" -o tsv)
Write-Host ""
Write-Host "============================================================"
Write-Host " Frontend is live at: $url"
Write-Host " API base baked into this build: $ApiBase"
Write-Host "============================================================"
