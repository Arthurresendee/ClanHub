# Testes do ClanHub

Quatro camadas, todas rodam com `dotnet test ClanHub.slnx`.

| Projeto | O que cobre | Precisa de |
| --- | --- | --- |
| `ClanHub.UnitTests` | Regras puras (tags, permissões, comparador de membros, medalhas da CWL, agenda da Capital, validação de webhook), handlers de sincronização (EF InMemory), `SyncProcessor` (erros, backoff, reagendamento) e os clientes da Supercell contra o WireMock (`tests/fixtures`) | nada |
| `ClanHub.Client.Tests` | bUnit: componentes (`ClanBadge`, `PwaStatus`, `Home`), formatação e **arquivos da PWA** (manifest, ícones, service worker, `index.html`) | nada |
| `ClanHub.IntegrationTests` | API inteira com `WebApplicationFactory` e **SQL Server real**: login, permissões por papel, posse do clã, fila com reserva concorrente, sincronização ponta a ponta, CWL, Capital, recrutamento, alertas, Discord (regras de vínculo) e passkeys (autenticador WebAuthn virtual) | SQL Server (veja abaixo) |
| `ClanHub.E2E` | Playwright + Chromium real: app sobe no navegador, cadastro → confirmação por e-mail → login → workspace → verificação do clã → membros, todas as telas sem erro de runtime, mobile, tema escuro, recrutamento público, convite de equipe, exclusão de conta, **passkey no navegador** e **PWA publicada funcionando offline** | SQL Server + Chromium |

## SQL Server para os testes
Os testes que precisam de banco escolhem, nesta ordem:
1. variável de ambiente `CLANHUB_TEST_SQL` (string de conexão com acesso ao `master`; usada no CI);
2. **LocalDB** (`(localdb)\MSSQLLocalDB`), comum em máquinas Windows com Visual Studio;
3. Docker (Testcontainers), só nos testes de integração.

Cada execução cria um banco próprio (`ClanHubTest_*` / `ClanHubE2E_*`) e apaga no final. Sem nenhuma das opções, esses testes são **pulados** (não falham).

## Chromium do Playwright (E2E)
Uma vez, depois do primeiro build:
```
pwsh tests/ClanHub.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
```
Sem o navegador, os testes E2E são pulados.

## Dicas
- Rodar só uma camada: `dotnet test tests/ClanHub.UnitTests`.
- O E2E inicia o `ClanHub.Server` como processo próprio; a "Supercell" é simulada pelo WireMock e o SMTP por um servidor mínimo em memória (para ler o link de confirmação). Nada sai da sua máquina.
- Se um E2E for interrompido à força, pode sobrar um processo `dotnet ... ClanHub.Server.dll`; encerre-o antes de compilar de novo.
- As fixtures de `tests/fixtures` são **sintéticas**. Troque por respostas reais das APIs quando tiver as chaves e rode os testes: os DTOs toleram campos novos/ausentes.