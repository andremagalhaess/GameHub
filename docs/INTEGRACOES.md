# Integrações e chaves de API

Todas as chaves são informadas em **Configurações › Busca de informações online** e guardadas cifradas com o DPAPI do Windows (escopo do usuário). Um backup restaurado em outro PC ou outra conta do Windows não consegue ler as chaves: basta informá-las de novo.

## Já implementadas (Prioridade 1)

### Steam Store (sem chave)

- Usa os endpoints públicos da loja: `store.steampowered.com/api/storesearch` e `/api/appdetails`, em português (`l=brazilian`, `cc=BR`).
- Traz: nome, descrição completa e resumida, desenvolvedora, publicadora, gêneros, data, plataformas, nota do Metacritic, **classificação indicativa brasileira (ClassInd)** quando disponível, trailer, capa vertical, hero/background, logo e screenshots.
- Limite: a Steam aceita cerca de 200 requisições a cada 5 minutos. O GameHub faz no máximo 6 por busca.
- Observação: são endpoints públicos usados pela própria loja, mas não documentados oficialmente pela Valve. Se mudarem, a Steam pode ser desligada na mesma tela sem afetar o resto.

### RAWG (chave gratuita)

1. Crie uma conta em <https://rawg.io>.
2. Acesse <https://rawg.io/apidocs> e clique em **Get API Key**. Preencha o formulário (uso pessoal/não comercial).
3. Copie a chave e cole em Configurações › RAWG › **Salvar**. Use **Testar** para confirmar.

Termos: a RAWG pede atribuição. A página do jogo mostra "Dados fornecidos por RAWG" quando os dados vieram de lá.

### IGDB (Client ID + Client Secret do Twitch)

O IGDB pertence à Twitch e usa autenticação de aplicativo.

1. Entre em <https://dev.twitch.tv/console/apps> com uma conta Twitch (ative a autenticação em dois fatores, é obrigatória).
2. **Register Your Application**: nome qualquer (ex.: *GameHub pessoal*), OAuth Redirect URL `http://localhost`, categoria *Application Integration*, tipo de cliente **Confidential**.
3. Abra o app criado, copie o **Client ID** e clique em **New Secret** para gerar o **Client Secret**.
4. Cole os dois em Configurações › IGDB › **Salvar** e use **Testar**.

O GameHub obtém e renova o token de acesso sozinho (fluxo *client credentials*) e o mantém só na memória. Limite do IGDB: 4 requisições por segundo.

## Plataformas (Prioridade 2)

Em **Biblioteca › Atualizar biblioteca**. Nada roda sozinho em segundo plano.

| Plataforma | Como detecta | Como inicia | Horas |
|---|---|---|---|
| Steam (instalados) | `libraryfolders.vdf` + `appmanifest_*.acf` | `steam://rungameid/ID` | via Web API (abaixo) |
| Steam (conta inteira) | Steam Web API oficial `IPlayerService/GetOwnedGames` | `steam://rungameid/ID` | **sim**, dado oficial da Steam |
| Epic Games Store | manifestos `*.item` em `ProgramData\Epic` | protocolo `com.epicgames.launcher://` | não (a Epic não oferece API) |
| GOG | registro `GOG.com\Games` | executável (DRM-free) | medidas pelo GameHub |
| Ubisoft Connect | registro `Ubisoft\Launcher\Installs` | `uplay://launch/ID/0` | não |
| EA app, Battle.net, Rockstar, Amazon | "Programas instalados" do Windows (editor da entrada) | executável registrado pelo instalador | medidas pelo GameHub quando o EXE é o próprio jogo |
| Riot (VALORANT, LoL) | "Programas instalados" | `RiotClientServices.exe --launch-product=…` | não |
| Xbox / PC Game Pass / Microsoft Store | pastas `XboxGames` com `MicrosoftGame.config` | executável do jogo | medidas pelo GameHub |

### Steam Web API (opcional)

1. Gere a chave em <https://steamcommunity.com/dev/apikey> (domínio: qualquer, ex. `localhost`).
2. Descubra seu **SteamID64** (17 dígitos, começa com 7656): aparece na URL do perfil quando não há URL personalizada, ou em sites como steamid.io.
3. No perfil Steam, em *Privacidade*, deixe **Detalhes de jogos** como *Público*.
4. Cole chave e SteamID em Configurações › Plataformas e launchers.

Associação: se o mesmo jogo aparece em várias lojas (ou você já tinha adicionado pelo EXE), o GameHub junta tudo em uma entrada pelo executável ou pelo nome normalizado. Na página do jogo, *Plataformas vinculadas* mostra cada loja e permite desvincular.
| TheGamesDB | Avaliar | Exige chave solicitada no fórum do projeto. |
| Wikidata | Avaliar | Sem chave (SPARQL público). |
| HowLongToBeat | Prioridade 4 | **Não há API oficial.** Só será integrado se existir um meio permitido; caso contrário, campo manual. |
| Backloggd | Prioridade 4 | **Não há API oficial.** Importação/exportação por CSV, sem sincronização simulada. |
