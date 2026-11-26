# Compilação, execução e distribuição

## Pré-requisitos

- Windows 10 19041+ ou Windows 11.
- .NET SDK 10 (`winget install Microsoft.DotNet.SDK.10`).
- Opcional: Visual Studio 2022 17.14+ ou 2026, cargas *Desenvolvimento para desktop com .NET* e *Desenvolvimento de aplicativos WinUI*.

O projeto do app é **não empacotado** e **autocontido** (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`): o Windows App SDK e o runtime .NET vão junto com o executável, então quem usar não precisa instalar nada.

## Linha de comando

```powershell
dotnet test tests\GameHub.Core.Tests                       # testes
dotnet build src\GameHub.App -c Debug -p:Platform=x64      # compilar
dotnet run   --project src\GameHub.App -p:Platform=x64     # executar
```

> O núcleo (`GameHub.Core`) e os testes compilam em qualquer sistema. O app WinUI 3 só compila no Windows (o compilador XAML é exclusivo do Windows).

## Visual Studio

Abra `GameHub.slnx` (Visual Studio 2022 17.13+ ou 2026), defina **GameHub.App** como projeto de inicialização, plataforma **x64**, e pressione F5.

## Versão portátil

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1          # x64
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -Arch ARM64
```

Gera `publish\GameHub-win-x64\GameHub.exe`. A pasta pode ser copiada para qualquer lugar.

## Integração contínua

O workflow `.github/workflows/build.yml` roda os testes e compila o app em `windows-latest` a cada push/PR, e publica a pasta portátil como artefato.

## Instalador

Previsto para a entrega final (Prioridade 4): MSIX assinado ou instalador Inno Setup a partir da pasta portátil. Os dados do usuário ficam em `%LOCALAPPDATA%\GameHub` e não são tocados por atualizações.

## Problemas conhecidos

- Os seletores de arquivo do Windows não abrem se o GameHub for executado **como administrador**. Rode normalmente (o app não precisa de administrador; jogos que pedem elevação mostram o UAC ao clicar em JOGAR).
