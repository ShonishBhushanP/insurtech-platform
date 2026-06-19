<#
  Launches all backend services + the API gateway, each in its own window.
  Requires the .NET 8 SDK on PATH (install separately, then re-open the shell).
  Ports: Policy 5101 · Claims 5102 · Fraud 5103 · Documents 5104 · Payments 5105 · Partner 5106 · Gateway 8080
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root "backend\src"

$services = @(
  @{ Name = "Policy";    Path = "Services\Policy\Policy.Api" },
  @{ Name = "Claims";    Path = "Services\Claims\Claims.Api" },
  @{ Name = "Fraud";     Path = "Services\Fraud\Fraud.Api" },
  @{ Name = "Documents"; Path = "Services\Documents\DocMgmt.Api" },
  @{ Name = "Payments";  Path = "Services\Payments\Payments.Api" },
  @{ Name = "Partner";   Path = "Services\Partner\Partner.Api" },
  @{ Name = "Underwriting"; Path = "Services\Underwriting\Underwriting.Api" },
  @{ Name = "Notification"; Path = "Services\Notification\Notification.Api" },
  @{ Name = "Audit";     Path = "Services\Audit\Audit.Api" },
  @{ Name = "Gateway";   Path = "Gateway\InsurTech.Gateway" }
)

foreach ($svc in $services) {
  $proj = Join-Path $src $svc.Path
  Write-Host "Starting $($svc.Name) → $proj"
  Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$proj'; dotnet run"
  Start-Sleep -Milliseconds 800
}

Write-Host ""
Write-Host "All services launching. Gateway: http://localhost:8080  (Swagger per service on its own port)."
