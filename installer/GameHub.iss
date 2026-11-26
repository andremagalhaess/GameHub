; Instalador do GameHub (Inno Setup 6).
; Compilar:  ISCC.exe installer\GameHub.iss /DAppVersion=1.0.0 /DSourceDir=..\publish\GameHub-win-x64
; Instala só para o usuário atual (sem pedir administrador), cria atalho no Menu Iniciar e,
; se você quiser, na Área de Trabalho. Não inicia com o Windows e não instala serviços.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\GameHub-win-x64"
#endif

[Setup]
AppId={{6F2B1C7E-5B7A-4C1E-9E3D-6A0B7C1D2E40}
AppName=GameHub
AppVersion={#AppVersion}
AppVerName=GameHub {#AppVersion}
AppPublisher=GameHub
DefaultDirName={autopf}\GameHub
DefaultGroupName=GameHub
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\publish
OutputBaseFilename=GameHub-Setup-{#AppVersion}-x64
SetupIconFile=..\src\GameHub.App\Assets\GameHub.ico
UninstallDisplayIcon={app}\GameHub.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GameHub"; Filename: "{app}\GameHub.exe"
Name: "{group}\Desinstalar o GameHub"; Filename: "{uninstallexe}"
Name: "{autodesktop}\GameHub"; Filename: "{app}\GameHub.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GameHub.exe"; Description: "Abrir o GameHub agora"; Flags: nowait postinstall skipifsilent

; Os seus dados (%LOCALAPPDATA%\GameHub: banco, imagens, backups) NÃO são apagados na desinstalação.
