# GameHub

Launcher universal de jogos e diário gamer pessoal para **Windows 11**. Nativo (C# / .NET 10 + WinUI 3), leve, com banco de dados local SQLite e sem conta online.

> Estado atual: **Prioridades 1 e 2 concluídas**. As prioridades 3 e 4 estão listadas em [Roadmap](#roadmap).

## O que já funciona

| Área | O que faz |
|---|---|
| **Interface** | Janela nativa do Windows 11 com efeito Mica, barra de título integrada, sidebar (Início, Biblioteca, Adicionar jogo, Meu Perfil, Configurações), tema escuro padrão, tema claro, cor de destaque personalizável. |
| **Adicionar jogo por EXE** | Seleciona qualquer `.exe`, lê Product Name, File Description, Company Name, Original Filename, versão e pasta de instalação e sugere o nome (ex.: `Cyberpunk2077.exe` → *Cyberpunk 2077*). O nome é editável. **O arquivo nunca é executado no cadastro.** |
| **Busca online** | Botão **BUSCAR INFORMAÇÕES ONLINE** pesquisa ao mesmo tempo na Steam (sem chave), RAWG e IGDB (com chave). Mostra capa, nome, ano, desenvolvedora e resumo de cada resultado. Nada é adicionado sem você escolher. |
| **Confirmação** | Todos os campos editáveis (nome, descrições, desenvolvedora, publicadora, gêneros, tags, plataformas, data, classificação, site, trailer, status, horas, executável, argumentos, pasta de trabalho, ícone) + galerias para escolher capa, background e logo. Só as artes escolhidas são baixadas, e ficam guardadas localmente (funcionam offline). |
| **Offline** | Sem internet, o jogo pode ser adicionado manualmente e completado depois em *Atualizar informações online*. |
| **Biblioteca** | Grade de capas virtualizada, busca, filtro por status, favoritos, ordenação. Detecção de duplicados (mesmo EXE, mesmo jogo online ou mesmo nome). Remover da biblioteca não apaga nem desinstala nada. |
| **Página do jogo** | Background e logo em destaque, **JOGAR**, favoritar, status (Backlog, Jogando, Zerado, 100%, Abandonado, Pausado), editar, atualizar informações online, alterar capa/background, abrir pasta, remover. |
| **JOGAR** | Executa o EXE com argumentos e pasta de trabalho configuráveis. Mede o tempo de sessão sem polling (espera o processo fechar). Se o EXE fechar em menos de 1 minuto (launcher intermediário), a sessão **não** é contada. |
| **Perfil** | Local: nome, usuário, foto, banner, bio, cor de destaque, jogo atual. Estatísticas reais da biblioteca e prateleiras de favoritos, em andamento, zerados e abandonados. |
| **Plataformas** | *Atualizar biblioteca* detecta jogos instalados de Steam, Epic, GOG, Ubisoft Connect, EA app, Battle.net, Riot, Rockstar, Amazon Games e Xbox/PC Game Pass, lendo arquivos e registros locais (sem senha). Com a Steam Web API (opcional), importa também jogos não instalados e as horas oficiais. Um jogo presente em várias lojas vira uma única entrada; jogos removidos não voltam; desinstalados são marcados, nunca apagados. |
| **Avaliações** | Nota de 0 a 10 (meio ponto), review, pontos positivos e negativos, momentos memoráveis, comentários, tags, datas de início e conclusão. Editável a qualquer momento. |
| **Filtros** | Nome, status, gênero, plataforma, nota mínima, tempo jogado, favoritos; ordenação por nome, adição, último jogo, horas, lançamento e nota. |
| **Configurações** | Tema, transparência, animações, tamanho das capas, cor; chaves RAWG/IGDB (cifradas com DPAPI); cache de imagens; **backup e restauração** completos (.zip). |

## Requisitos

- Windows 11 (funciona também no Windows 10 19041+), x64 ou ARM64.
- Para compilar: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2022/2026 com a carga *Desenvolvimento para desktop com .NET* + *WinUI* é opcional (útil para depurar).

## Compilar e executar

```powershell
git clone https://github.com/andremagalhaess/GameHub.git
cd GameHub

# testes do núcleo
dotnet test tests\GameHub.Core.Tests

# executar o app (Debug)
dotnet run --project src\GameHub.App -p:Platform=x64

# gerar a versão portátil (pasta com GameHub.exe, sem instalar nada)
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

Mais detalhes em [docs/COMPILACAO.md](docs/COMPILACAO.md).

## Chaves de API

A Steam funciona sem configuração. RAWG e IGDB precisam de credenciais gratuitas, informadas em **Configurações › Busca de informações online**. Passo a passo em [docs/INTEGRACOES.md](docs/INTEGRACOES.md).

## Onde ficam os dados

`%LOCALAPPDATA%\GameHub\`

- `gamehub.db`: banco SQLite (jogos, perfil, sessões, configurações).
- `images\`: capas, backgrounds, logos, ícones e fotos (cache local).
- `backups\`: cópias de segurança automáticas antes de cada restauração.
- `logs\`: apenas erros.

O esquema do banco é versionado (`PRAGMA user_version`) e migrado automaticamente, sem perder dados em atualizações.

## Privacidade e desempenho

- Sem conta online, sem telemetria, sem senhas de launchers.
- Chaves de API cifradas com DPAPI (só a sua conta do Windows consegue ler).
- Nada roda em segundo plano: buscas online só quando você clica, sem bandeja, sem iniciar com o Windows.
- Imagens decodificadas no tamanho exibido e carregadas sob demanda; lista virtualizada. O teste de desempenho carrega 5.000 jogos.

## Estrutura

```
src/GameHub.Core     núcleo (.NET 10, sem dependência de Windows): banco, EXE, metadados, imagens, execução, backup
src/GameHub.App      interface WinUI 3 (Windows)
tests/GameHub.Core.Tests  testes automatizados (xUnit)
docs/                documentação
```

Arquitetura e decisões em [docs/ARQUITETURA.md](docs/ARQUITETURA.md).

## Roadmap

- **Prioridade 3**: Minha Jornada, estatísticas, timeline, Meu Ano Gamer, rankings, calendário, memórias.
- **Prioridade 4**: HowLongToBeat, Backloggd (CSV), importação/exportação, instalador, otimizações.
