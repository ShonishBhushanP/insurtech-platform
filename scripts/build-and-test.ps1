<#
  Restores, builds, and tests the backend solution, producing a code-coverage report.
  Requires the .NET 8 SDK on PATH.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sln  = Join-Path $root "backend\InsurTech.sln"

Write-Host "==> Restoring"
dotnet restore $sln

Write-Host "==> Building (Release)"
dotnet build $sln -c Release --no-restore

Write-Host "==> Testing with coverage"
dotnet test $sln -c Release --no-build --collect:"XPlat Code Coverage" --results-directory (Join-Path $root "artifacts\coverage")

Write-Host ""
Write-Host "Coverage written under artifacts\coverage\**\coverage.cobertura.xml"
Write-Host "Optional HTML report:  dotnet tool install -g dotnet-reportgenerator-globaltool; reportgenerator -reports:artifacts\coverage\**\coverage.cobertura.xml -targetdir:artifacts\coverage-report"
