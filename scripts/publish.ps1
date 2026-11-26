# Gera a versão portátil do GameHub (pasta com GameHub.exe, sem precisar instalar .NET nem Windows App SDK).
# Uso:  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 [-Arch x64|ARM64]
param([ValidateSet("x64", "ARM64")] [string]$Arch = "x64")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$rid = if ($Arch -eq "ARM64") { "win-arm64" } else { "win-x64" }
$out = Join-Path $root "publish\GameHub-$rid"

dotnet test (Join-Path $root "tests\GameHub.Core.Tests") -c Release
if ($LASTEXITCODE -ne 0) { throw "Testes falharam." }

dotnet publish (Join-Path $root "src\GameHub.App\GameHub.App.csproj") -c Release -p:Platform=$Arch -r $rid -o $out
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar." }

Write-Host ""
Write-Host "Pronto: $out\GameHub.exe" -ForegroundColor Green
