# GameHub

Launcher universal de jogos e diário gamer pessoal para **Windows 11**. Nativo (C# / .NET 10 + WinUI 3), leve, com banco de dados local SQLite e sem conta online.

> Versão **1.0.0**: prioridades 1 a 4 da especificação concluídas.

## O que já funciona

| Área | O que faz |
|---|---|
| **Interface** | Janela nativa do Windows 11 com efeito Mica, barra de título integrada, sidebar (Início, Biblioteca, Adicionar jogo, Minha Jornada, Meus Rankings, Meu Perfil, Configurações), tema escuro padrão, tema claro, cor de destaque personalizável. |
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
| **Minha Jornada** | Painel com cards (cadastrados, zerados, em andamento, abandonados, horas, média das notas, gênero favorito, plataforma mais usada, jogo mais bem avaliado) e gráficos de zerados por ano, gêneros e status. **Linha do tempo** agrupada por ano, com filtros por ano, mês ou período personalizado, status, gênero, plataforma, nota e horas. **Meu Ano Gamer**: retrospectiva do ano (jogados, começados, zerados, horas medidas, melhor e pior jogo, gênero e plataforma, zerados por mês, favoritos, reviews) com **exportação em PNG**. **Calendário** mensal com inícios, conclusões, sessões e memórias de cada dia. |
| **Memórias** | Na página de cada jogo: screenshots, imagens, textos, momentos favoritos, cenas marcantes e observações, com data opcional. As imagens são copiadas para a pasta do GameHub (o original não é alterado). |
| **Meus Rankings** | 7 rankings iniciais (Top 10 favoritos, melhores de todos os tempos, do ano, histórias, trilhas sonoras, multiplayer, mais marcantes) e rankings próprios. Arrastar e soltar (ou botões subir/descer) para reordenar, limite opcional, **exportação em PNG**. |
| **Importar e exportar** | CSV de jogos, reviews, jornada e rankings (abre no Excel). Importação sem duplicar, com relatório de linhas com problema. |
| **Backloggd** | Importa o CSV exportado do Backloggd (estrelas viram nota 0–10, status mapeados) e exporta avaliações e histórico em formato compatível. Sem sincronização falsa: o Backloggd não tem API pública. |
| **HowLongToBeat** | Card *Quanto tempo leva* na página do jogo, com as estimativas que você informa e link direto para o site (que não tem API pública). |
| **Configurações** | Tema, transparência, animações, tamanho das capas, cor; chaves RAWG/IGDB (cifradas com DPAPI); cache de imagens; **backup e restauração** completos (.zip). |

## Requisitos

- Windows 11 (funciona também no Windows 10 19041+), x64 ou ARM64.
- Para compilar: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2022/2026 com a carga *Desenvolvimento para desktop com .NET* + *WinUI* é opcional (útil para depurar).

## Instalar

Baixe o `GameHub-Setup-<versão>-x64.exe` (artefato **GameHub-Setup-x64** do último build em *Actions*, ou gere com `scripts\publish.ps1 -Installer`) e execute. Ele instala só para o seu usuário, sem administrador, e cria o atalho no Menu Iniciar. Como o instalador não é assinado, o SmartScreen pode pedir *Mais informações › Executar assim mesmo*.

Prefere sem instalar? Use a pasta portátil (artefato **GameHub-win-x64**) e abra `GameHub.exe`.

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

- `gamehub.db`: banco SQLite (jogos, perfil, sessões, reviews, memórias, rankings, configurações).
- `images\`: capas, backgrounds, logos, ícones, fotos e imagens das memórias.
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

## Testes

`dotnet test tests\GameHub.Core.Tests` roda 80 testes automatizados. Eles cobrem a adição por EXE (sem executar), a identificação de nomes, a busca online com respostas simuladas, a escolha de resultados, o cache de imagens, a execução e medição de sessões e a importação das plataformas. Também cobrem duplicados, perfil, avaliações, Minha Jornada, rankings, backup e restauração, o funcionamento offline, a importação e exportação CSV e o desempenho com 3.000 a 5.000 jogos.
