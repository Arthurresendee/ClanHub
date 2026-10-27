# ClanHub

Painel web gratuito de gestão de clãs (Clash of Clans, Clash Royale, Brawl Stars) usando as APIs oficiais da Supercell.
O plano completo está em docs/PLANO.md (quando existir). Leia a seção indicada antes de cada tarefa.

## Stack e hospedagem
- .NET 10, C# 14, ASP.NET Core Minimal APIs, Blazor WebAssembly + MudBlazor
- EF Core 10 + SQL Server (já existente numa VPS, acessado pela internet com TLS)
- Produção: um contêiner na Vercel (Dockerfile.vercel + vercel.json) que entrega o Blazor e a API
- Desenvolvimento: Aspire (SQL Server local em contêiner) — `dotnet run --project src/ClanHub.AppHost`
- Testes: xUnit, Testcontainers (precisa de Docker), WireMock.Net, bUnit

## Restrições da Vercel (respeitar sempre)
- Não existe processo contínuo: nada de BackgroundService, Timer ou Task.Run solto. Trabalho periódico vai para /internal/* chamado por cron.
- Endpoints /internal/* exigem Authorization: Bearer com CRON_SECRET, aceitam GET e POST e são idempotentes.
- Sem SignalR: atualização das telas por polling em /events?afterId=.
- Nada gravado em disco; logs só no console.
- Ler a porta da variável PORT (padrão 8080).
- Chamadas à Supercell sempre pela URL base configurada (proxy com IP fixo).

## Regras de arquitetura
- Dependências: Domain <- Application <- (Infrastructure, Supercell) <- Sync <- Server. Client depende só de Contracts.
- Todo acesso à Supercell passa por ClanHub.Supercell e IGameConnector. Nunca chamar a Supercell a partir do Client.
- Rotas de negócio em /api/v1, erros em ProblemDetails, paginação por cursor.
- Todo endpoint de workspace ou clã usa `.RequireWorkspaceRole(papel)`.
- Datas em UTC no banco e na API. IDs Guid v7 (Guid.CreateVersion7()).
- Migrations sempre compatíveis com a versão anterior do código (adicionar antes, remover depois). Nunca editar uma migration já aplicada.

## Regras de produto (não negociáveis)
- Nada pago: nenhum recurso pode depender de pagamento ou doação.
- Rodapé com o aviso da Supercell em todas as páginas.
- Não usar "Clash of Clans", "Clash Royale", "Brawl Stars" ou "Supercell" no nome do produto, em domínios ou em textos que sugiram parceria oficial.
- A API da Supercell é só leitura: não prometer ações dentro do jogo.

## Segredos
- Nunca escrever chaves, senhas ou strings de conexão em arquivos versionados.
- Desenvolvimento: dotnet user-secrets. Produção: variáveis de ambiente na Vercel. Migrations no CI: segredo DB_MIGRATOR_CONNECTION.

## Testes
- `dotnet test ClanHub.slnx` roda tudo (unitários, bUnit, integração com SQL Server real, E2E com Playwright). Detalhes em docs/TESTES.md.
- Bug corrigido nos testes E2E: o `index.html` do Client NÃO pode usar `#[.{fingerprint}]` (só vale no modo standalone); mantenha `_framework/blazor.webassembly.js`.
- `AuthorizeRouteView` recria a página quando o estado de autenticação é notificado: `ApiAuthStateProvider.RefreshAsync` só notifica quando a identidade muda (use `MeChanged` para o resto).
- Rotas opcionais da Supercell (Capital, ranking, perfis) que dão 404 NÃO marcam o clã como "não encontrado" (só as coletas principais).

## Fluxo
- Ao terminar: `dotnet build ClanHub.slnx` sem warnings e `dotnet test ClanHub.slnx` passando.
- Interface em português do Brasil.