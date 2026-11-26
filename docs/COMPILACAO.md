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

O instalador é gerado com o [Inno Setup 6](https://jrsoftware.org/isinfo.php) a partir da pasta portátil (script em `installer\GameHub.iss`):

```powershell
winget install JRSoftware.InnoSetup                                     # uma vez
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -Installer
```

Resultado: `publish\GameHub-Setup-<versão>-x64.exe`. O CI também gera esse arquivo (artefato **GameHub-Setup-x64** na página da execução, em *Actions*).

O que o instalador faz:

- Instala só para o seu usuário (`%LOCALAPPDATA%\Programs\GameHub`), sem pedir administrador.
- Cria atalho no Menu Iniciar e, se você marcar, na Área de Trabalho.
- **Não** adiciona inicialização com o Windows, bandeja ou serviços.
- Aparece em *Configurações › Aplicativos* para desinstalar. A desinstalação **não apaga** seus dados em `%LOCALAPPDATA%\GameHub`. Para apagar tudo, remova essa pasta manualmente.
- Para atualizar, rode o instalador da nova versão por cima. Os dados são mantidos e o banco é migrado automaticamente.

Por que Inno Setup e não MSIX: o MSIX exige assinar o pacote com um certificado confiável. Sem certificado comprado, o Windows bloqueia a instalação ou pede para instalar o certificado à mão. O Inno Setup gera um `.exe` comum. Como ele não é assinado, o SmartScreen pode mostrar "O Windows protegeu o computador" na primeira vez: clique em *Mais informações › Executar assim mesmo*.

## Problemas conhecidos

- Os seletores de arquivo do Windows não abrem se o GameHub for executado **como administrador**. Rode normalmente (o app não precisa de administrador; jogos que pedem elevação mostram o UAC ao clicar em JOGAR).
