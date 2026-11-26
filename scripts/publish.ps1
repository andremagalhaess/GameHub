# Gera a versão portátil do GameHub (pasta com GameHub.exe, sem precisar instalar .NET nem Windows App SDK).
# Uso:  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 [-Arch x64|ARM64] [-Installer]
# -Installer também gera publish\GameHub-Setup-<versão>-x64.exe (precisa do Inno Setup 6: winget install JRSoftware.InnoSetup)
param([ValidateSet("x64", "ARM64")] [string]$Arch = "x64", [switch]$Installer)

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

if ($Installer) {
    if ($Arch -ne "x64") { throw "O instalador é gerado para x64." }
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 não encontrado. Instale com: winget install JRSoftware.InnoSetup" }
    [xml]$proj = Get-Content (Join-Path $root "src\GameHub.App\GameHub.App.csproj")
    $version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    & $iscc (Join-Path $root "installer\GameHub.iss") "/DAppVersion=$version" "/DSourceDir=$out"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o instalador." }
    Write-Host "Instalador: $(Join-Path $root "publish\GameHub-Setup-$version-x64.exe")" -ForegroundColor Green
}
