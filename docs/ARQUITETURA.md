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
| Integrações com plataformas | `Core/Metadata` (novas fontes implementam `IMetadataProvider`); importadores de launchers em `Core/Platforms` |
| Identificação de executáveis | `Core/Executables` |
| Busca online de metadados | `Core/Metadata` |
| Gerenciamento de imagens | `Core/Images` |
| Perfil do usuário | `Core/Data/ProfileRepository`, `App/Pages/ProfilePage` |
| Banco de dados | `Core/Data` |
| Configurações | `Core/Data/SettingsRepository`, `App/Pages/SettingsPage` |
| Backup e restauração | `Core/Backup` |
| Inicialização de jogos | `Core/Launching` |
| Reviews | `Core/Data/ReviewRepository`, `App/Pages/GameDetailsPage` |
| Minha Jornada (painel, linha do tempo, Meu Ano Gamer, calendário) | `Core/Journey/JourneyService`, `App/Pages/JourneyPage` |
| Memórias | `Core/Data/MemoryRepository`, seção Memórias em `App/Pages/GameDetailsPage` |
| Rankings | `Core/Data/RankingRepository`, `App/Pages/RankingsPage` |
| Gráficos e exportação de imagem | `App/Controls/BarChart` (retângulos, sem biblioteca), `App/Helpers/ImageExport` (PNG via `RenderTargetBitmap`) |

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
4. **Minha Jornada só usa dados registrados.** A data de um jogo na linha do tempo é, nesta ordem: data de início que você informou, data de conclusão, ou a primeira sessão medida pelo GameHub. Sem nenhuma delas, o jogo aparece em "Sem data". As horas do *Meu Ano Gamer* são só as sessões medidas pelo GameHub naquele ano, porque as lojas informam apenas o total acumulado, sem dizer quando.
5. **Cor de destaque**: o GameHub aplica na hora nos próprios elementos; alguns controles nativos do Windows só mudam ao reabrir o app.
6. **Windows App SDK 1.8** (linha estável amplamente usada) em vez da 2.x, para reduzir riscos de compatibilidade.
7. **"Jogos zerados por ano"** conta jogos com status Zerado ou 100% **e** data de conclusão preenchida.
8. **Pior jogo do ano** só aparece quando há pelo menos 2 jogos com nota no ano.
9. **Rankings** aceitam só jogos da biblioteca. O "Top 10" tem limite de 10; os demais rankings iniciais não têm limite. O ano de "Melhores jogos do ano" é escolhido em Editar ranking.
