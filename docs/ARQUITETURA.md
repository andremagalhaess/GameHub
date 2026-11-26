# Arquitetura

## Visão geral

```
GameHub.App (WinUI 3)          GameHub.Core (.NET 10, portátil)
├─ MainWindow (sidebar)        ├─ Data/        SQLite: Database (migrações), GameRepository, ProfileRepository, SettingsRepository
├─ Pages/                      ├─ Executables/ ExeAnalyzer (VERSIONINFO + pasta → nome sugerido)
│  Home, Library, AddGame,     ├─ Metadata/    IMetadataProvider: SteamStoreProvider, RawgProvider, IgdbProvider; MetadataService
│  GameDetails, GameEdit,      ├─ Images/      ImageCache (download sob demanda, deduplicação por hash, limpeza)
│  Profile, Settings, Welcome  ├─ Launching/   GameLauncher (Process + medição de sessão)
├─ Controls/                   ├─ Security/    SecretStore + DpapiSecretProtector
│  GameCoverCard, GameEditor   ├─ Backup/      BackupService (.zip com banco + imagens)
└─ Services/                   └─ Text/        normalização de títulos, HTML → texto, datas
   AppServices, ThemeService,
   IconExtractor, Log
```

Correspondência com os módulos pedidos na especificação:

| Módulo | Onde |
|---|---|
| Interface gráfica | `GameHub.App` |
| Biblioteca de jogos | `Core/Data/GameRepository` |
| Integrações com plataformas | `Core/Metadata` (novas fontes implementam `IMetadataProvider`); importadores de launchers entram na Prioridade 2 em `Core/Platforms` |
| Identificação de executáveis | `Core/Executables` |
| Busca online de metadados | `Core/Metadata` |
| Gerenciamento de imagens | `Core/Images` |
| Perfil do usuário | `Core/Data/ProfileRepository`, `App/Pages/ProfilePage` |
| Banco de dados | `Core/Data` |
| Configurações | `Core/Data/SettingsRepository`, `App/Pages/SettingsPage` |
| Backup e restauração | `Core/Backup` |
| Inicialização de jogos | `Core/Launching` |
| Reviews, Minha Jornada, Rankings | Prioridades 2 e 3 |

## Decisões

- **SQL puro com Microsoft.Data.Sqlite** em vez de Entity Framework: menos memória e inicialização mais rápida.
- **Sem MVVM framework**: páginas com code-behind e `x:Bind`, menos dependências.
- **Imagens** decodificadas no tamanho exibido (`DecodePixelWidth`) e grade virtualizada.
- **Nada em segundo plano**: o tempo de jogo é medido aguardando o evento de saída do processo (sem timers).
- **Um único `HttpClient`** com timeout de 20 s e conexões ociosas liberadas em 30 s.

## Suposições (a confirmar)

1. **Status inicial "Sem status"** ao adicionar: o jogo não é colocado no Backlog automaticamente; você escolhe na tela de confirmação.
2. **Sessões com menos de 1 minuto não contam** (provável launcher intermediário). Jogos iniciados por launchers (ex.: EXE que abre a Epic) podem não ter o tempo medido; nesses casos as horas podem ser registradas manualmente.
3. **Nota externa** é mostrada na escala 0–100 (Metacritic, IGDB; nota de usuários RAWG convertida de 0–5).
4. **Minha Jornada e Meus Rankings** ficam fora da sidebar até serem implementados (Prioridade 3), para não exibir telas vazias.
5. **Cor de destaque**: o GameHub aplica na hora nos próprios elementos; alguns controles nativos do Windows só mudam ao reabrir o app.
6. **Windows App SDK 1.8** (linha estável amplamente usada) em vez da 2.x, para reduzir riscos de compatibilidade.
