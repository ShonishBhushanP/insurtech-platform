<#
  Deploy the Claims adjudication Durable Functions app (Claims.Functions) to Azure.
  Creates: a Storage account (Durable task hub) + a Consumption Function App (.NET 8 isolated),
  sets the downstream service URLs, and zip-deploys the published build (no 'func' CLI needed).

  Needs only Contributor on the resource group (no role assignments). Run right after:
      az login --use-device-code
      ./deploy-functions.ps1 -ResourceGroup rg-azuser7069_mml.local-yyRMB `
          -ClaimsUrl https://<claims-public> -FraudUrl https://<fraud-public> -PaymentsUrl https://<payments-public>

  After deploy, point Claims at it so the saga runs as Durable Functions:
      Claims app settings:  Claims__Adjudication__Mode = DurableFunctions
                            Claims__Adjudication__FunctionsBaseUrl = https://<functionapp-host>
#>
param(
  [Parameter(Mandatory = $true)] [string]$ResourceGroup,
  [string]$Location = "centralindia",
  [string]$ClaimsUrl = "http://localhost:5102",
  [string]$FraudUrl = "http://localhost:5103",
  [string]$PaymentsUrl = "http://localhost:5105"
)
$ErrorActionPreference = "Continue"
$az = (Get-Command az -ErrorAction SilentlyContinue).Source
if (-not $az) { $az = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd" }
$env:REQUESTS_CA_BUNDLE = "$env:USERPROFILE\az-cacert.pem"

$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo "backend\src\Services\Claims\Claims.Functions"
$publish = Join-Path $proj "publish"
$zip = Join-Path $proj "functions.zip"

# 1. Publish + zip the Functions app
Write-Host "==> Publishing Claims.Functions"
& "C:\Program Files\dotnet\dotnet.exe" publish $proj -c Release -o $publish
if ($LASTEXITCODE -ne 0) { Write-Host "publish failed"; exit 1 }
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force

# 2. Names (RG-derived, globally unique)
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = [System.BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($ResourceGroup))).Replace('-', '').ToLower().Substring(0, 8)
$stg = "itfn$hash"            # storage for the task hub
$app = "insurtech-fn-$hash"   # function app

# 3. Storage account (Durable task hub state)
Write-Host "==> Storage account $stg"
& $az storage account create -n $stg -g $ResourceGroup -l $Location --sku Standard_LRS -o none
if ($LASTEXITCODE -ne 0) { Write-Host "storage create failed (token? run az login)"; exit 1 }

# 4. Consumption Function App (.NET 8 isolated, Functions v4, Linux)
Write-Host "==> Function App $app"
& $az functionapp create -n $app -g $ResourceGroup -s $stg --consumption-plan-location $Location `
  --runtime dotnet-isolated --runtime-version 8.0 --functions-version 4 --os-type Linux -o none
if ($LASTEXITCODE -ne 0) { Write-Host "functionapp create failed"; exit 1 }

# 5. Downstream service URLs (config keys read by the activities)
& $az functionapp config appsettings set -n $app -g $ResourceGroup --settings `
  "Services:Claims=$ClaimsUrl" "Services:Fraud=$FraudUrl" "Services:Payments=$PaymentsUrl" -o none

# 6. Zip-deploy the build
Write-Host "==> Deploying package"
& $az functionapp deployment source config-zip -n $app -g $ResourceGroup --src $zip -o none
if ($LASTEXITCODE -ne 0) { Write-Host "zip deploy failed"; exit 1 }

$fnHost = & $az functionapp show -n $app -g $ResourceGroup --query "defaultHostName" -o tsv
Write-Host ""
Write-Host "============================================================"
Write-Host " Function App: https://$fnHost"
Write-Host " Starter endpoint: https://$fnHost/api/adjudications"
Write-Host ""
Write-Host " Now set on the Claims service:"
Write-Host "   Claims__Adjudication__Mode = DurableFunctions"
Write-Host "   Claims__Adjudication__FunctionsBaseUrl = https://$fnHost"
Write-Host "============================================================"
