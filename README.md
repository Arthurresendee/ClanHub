# ClanHub

Painel web gratuito para líderes e staff acompanharem clãs de jogos mobile (dados das APIs oficiais, somente leitura).
.NET 10 · ASP.NET Core Minimal APIs · Blazor WebAssembly + MudBlazor · EF Core + SQL Server · **PWA instalável**.

> Conteúdo de fã, sem afiliação com a Supercell. Gratuito (anúncios/doações não liberam recursos). O aviso da Supercell fica no rodapé de todas as páginas.

## Rodando localmente
Pré-requisitos: .NET 10 SDK e Docker (para o SQL Server do Aspire) — ou um SQL Server qualquer.

```
dotnet user-secrets set "Supercell:ClashOfClans:ApiKey" "..." --project src/ClanHub.Server
dotnet run --project src/ClanHub.AppHost          # sobe SQL Server + Mailpit + Server (com painel do Aspire)
```
Sem Aspire: aponte `ConnectionStrings:Default` para um SQL Server (user-secrets) e rode `dotnet run --project src/ClanHub.Server` (em Development as migrations são aplicadas sozinhas).
As chaves da Supercell só funcionam a partir do IP cadastrado: por padrão as chamadas passam pelo proxy da RoyaleAPI (`45.79.218.79`), veja `docs/DEPLOY.md`.

## Documentação
- `docs/COMECANDO.md` — **comece por aqui**: instalar, conectar ao SQL Server, migrations, rodar, criar conta.
- `docs/DEPLOY.md` — Vercel, banco, variáveis de ambiente, cron.
- `docs/TESTES.md` — as quatro camadas de testes e como rodá-las.
- `CLAUDE.md` — regras do projeto para o Claude Code.

## PWA
Manifest, ícones (any + maskable), service worker com cache do "casco" do app, aviso de falta de conexão e botão "Instalar app".
A API (`/api`, `/internal`) **nunca** é guardada em cache. Os testes cobrem os arquivos da PWA, o service worker de produção e o funcionamento offline em Chromium real.

## O que o plano cobre
| Item do plano | Situação |
| --- | --- |
| Etapas 1–8 (esqueleto, login, clientes da Supercell, workspaces e posse, sincronização por cron, telas do clã, guerras e Guerra de Clãs, deploy) | feito |
| Fase 2: CWL (planejamento e medalhas), Capital, temporada + CSV, notas/strikes, recrutamento com página pública, alertas (painel e Discord), login com Discord, configurações do workspace e do clã | feito |
| Fase 3: ranking, histórico de batalhas, torneios e baús (CR), calendário, passkeys, Redis opcional como cache compartilhado | feito |
| Confirmação de e-mail | pronta, mas **desligada em desenvolvimento** (`Auth:RequireConfirmedEmail`); em produção fica ligada por padrão. O envio real de e-mails (via n8n) é um item **futuro do plano** |
| App Flutter | substituído pela **PWA** (decisão desta etapa) |
| SignalR com serviço gerenciado | **não feito** — o plano mantém o polling de 20 s por causa das funções da Vercel; só vale reavaliar com um serviço gerenciado |
| Textos 100% em `.resx` | parcial — layout e navegação usam `.resx`; as páginas ainda têm texto no código |

## Antes de abrir ao público
Veja o checklist do plano e `docs/DEPLOY.md`: nome/domínio sem marcas da Supercell, **Termos e Privacidade revisados por um advogado** (os textos atuais são rascunhos), SPF/DKIM, backup do banco com restauração testada e monitor em `/health`.