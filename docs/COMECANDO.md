# Começando do zero

Guia passo a passo para rodar o ClanHub na sua máquina, conectar ao SQL Server que já existe e aplicar as migrations.
Escrito para quem não mexe com C# há um tempo: cada comando vem com o que ele faz.

## 1. O que instalar (uma vez)
- **.NET 10 SDK** — confira com `dotnet --version` (deve mostrar `10.x`).
- **Git** e um editor (VS Code com a extensão *C# Dev Kit*, ou Visual Studio).
- Docker **não** é necessário para o que está neste guia.

## 2. Baixar e compilar
Na pasta do projeto (onde está o arquivo `ClanHub.slnx`):

```
dotnet restore          # baixa os pacotes NuGet
dotnet build ClanHub.slnx   # compila tudo (deve terminar com "0 Erro(s)")
dotnet tool restore     # instala as ferramentas do projeto (o "dotnet ef", usado nas migrations)
```

## 3. Conectar ao SQL Server
A string de conexão é um **segredo** (tem senha), então **não vai em arquivo do projeto**. Ela fica nos *user-secrets*, um arquivo só seu, fora do repositório:

```
dotnet user-secrets set "ConnectionStrings:Default" "Server=ENDERECO,PORTA;Database=ClanHub;User Id=USUARIO;Password=SENHA;Encrypt=True;TrustServerCertificate=True;Max Pool Size=20" --project src/ClanHub.Server
```

| Parte | Significado |
| --- | --- |
| `Server=ENDERECO,PORTA` | endereço e porta do SQL Server (note a **vírgula** antes da porta) |
| `Database=ClanHub` | nome do banco — ele precisa **existir** (crie vazio se for novo) |
| `User Id` / `Password` | login do SQL Server |
| `Encrypt=True;TrustServerCertificate=True` | conexão criptografada, aceitando o certificado do servidor (ok em desenvolvimento) |
| `Max Pool Size=20` | limite de conexões simultâneas |

Para conferir o que foi salvo (cuidado: mostra a senha): `dotnet user-secrets list --project src/ClanHub.Server`.
O arquivo fica em `%APPDATA%\Microsoft\UserSecrets\clanhub-server\secrets.json`.
Para apagar: `dotnet user-secrets remove "ConnectionStrings:Default" --project src/ClanHub.Server`.

> O servidor SQL precisa ser alcançável da sua máquina (porta liberada / VPN). Se a conexão falhar, veja "Problemas comuns" no fim.

## 4. Migrations (criar as tabelas no banco)
*Migration* = um arquivo C# que descreve uma mudança no banco (criar tabela, adicionar coluna…). Elas ficam em `src/ClanHub.Infrastructure/Persistence/Migrations`.

### Jeito automático (desenvolvimento)
Ao rodar o servidor em **Development**, ele aplica as migrations pendentes sozinho (`Database:MigrateOnStartup` é `true` no `appsettings.Development.json`). Na primeira execução você verá no log `Applying migration ...`. O login precisa ter permissão de criar tabelas.
Em **produção** isso fica desligado de propósito: ali as migrations são aplicadas pelo CI (veja `docs/DEPLOY.md`).

### Jeito manual (para aprender e para produção)
O comando `dotnet ef` **não lê os user-secrets**; ele lê a variável de ambiente `ConnectionStrings__Default` (dois sublinhados). No PowerShell:

```
$env:ConnectionStrings__Default = "Server=ENDERECO,PORTA;Database=ClanHub;User Id=USUARIO;Password=SENHA;Encrypt=True;TrustServerCertificate=True"
dotnet ef database update --project src/ClanHub.Infrastructure --startup-project src/ClanHub.Infrastructure
```
(A variável vale só para aquela janela do terminal.)

Outros comandos úteis (todos com os mesmos `--project`/`--startup-project`):

| O que fazer | Comando |
| --- | --- |
| Ver quais migrations existem e quais já foram aplicadas | `dotnet ef migrations list ...` |
| Aplicar tudo que falta | `dotnet ef database update ...` |
| Voltar o banco para uma migration anterior | `dotnet ef database update NomeDaMigration ...` |
| Gerar um **script SQL** em vez de aplicar (para o DBA/revisar) | `dotnet ef migrations script --idempotent -o migracoes.sql ...` |

### Quando você mudar uma entidade (criar uma nova migration)
1. Altere a classe em `src/ClanHub.Domain` e a configuração em `src/ClanHub.Infrastructure/Persistence/AppDbContext.cs`.
2. Gere a migration, com um nome descritivo:
   ```
   dotnet ef migrations add NomeDescritivo --project src/ClanHub.Infrastructure --startup-project src/ClanHub.Infrastructure --output-dir Persistence/Migrations
   ```
3. **Leia o arquivo gerado** (principalmente se o aviso disser "may lead to data loss").
4. Aplique (`database update`) e rode os testes.
5. Regras do projeto: nunca edite uma migration já aplicada; deixe-as compatíveis com a versão anterior do código (adicione antes, remova depois).
Se errou antes de aplicar: `dotnet ef migrations remove --project src/ClanHub.Infrastructure --startup-project src/ClanHub.Infrastructure`.

## 5. Rodar o servidor
```
dotnet run --project src/ClanHub.Server
```
Ele mostra a porta no log (`Now listening on: ...`). Para fixar a porta, no PowerShell: `$env:ASPNETCORE_URLS = "http://localhost:5080"` antes do comando. Pare com `Ctrl+C`.
Verifique em `http://localhost:5080/health` (deve responder `Healthy`). A documentação interativa da API fica em `/scalar`.

## 6. Criar sua conta
1. Abra o site e vá em **Cadastrar**.
2. **Em desenvolvimento a confirmação de e-mail está desligada** (`Auth:RequireConfirmedEmail = false` no `appsettings.Development.json`): a conta já nasce confirmada e você entra direto. O envio real de e-mails (planejado via n8n) fica para depois; em produção a confirmação continua **ligada por padrão**. Se religar em desenvolvimento sem SMTP, o link de confirmação aparece no log do servidor (procure por `SMTP não configurado`).
3. Faça login. Para ter o papel de administrador da plataforma, defina `Admin:Email` com o seu e-mail nos user-secrets (`dotnet user-secrets set "Admin:Email" "voce@email.com" --project src/ClanHub.Server`) e reinicie.

## 7. Conectar à Supercell (para adicionar clãs)
1. Crie uma chave em cada portal (`developer.clashofclans.com`, `developer.clashroyale.com`, `developer.brawlstars.com`) cadastrando o IP **`45.79.218.79`** (proxy gratuito da RoyaleAPI; as chamadas saem por ele).
2. Guarde as chaves:
   ```
   dotnet user-secrets set "Supercell:ClashOfClans:ApiKey" "CHAVE" --project src/ClanHub.Server
   dotnet user-secrets set "Supercell:ClashRoyale:ApiKey" "CHAVE" --project src/ClanHub.Server
   dotnet user-secrets set "Supercell:BrawlStars:ApiKey" "CHAVE" --project src/ClanHub.Server
   ```
3. Reinicie o servidor.

## 8. Atualizar os dados dos clãs (sincronização)
Em produção um cron chama o servidor a cada minuto. Localmente não existe cron; dispare você mesmo:
```
dotnet user-secrets set "CRON_SECRET" "uma-senha-qualquer" --project src/ClanHub.Server
curl -X POST -H "Authorization: Bearer uma-senha-qualquer" http://localhost:5080/internal/sync/tick
```
(Repita o último comando sempre que quiser atualizar.)

## 9. Rodar os testes
```
dotnet test ClanHub.slnx
```
Detalhes (SQL Server dos testes, Chromium do Playwright) em `docs/TESTES.md`.

## Problemas comuns
| Sintoma | Causa e solução |
| --- | --- |
| `address already in use` ao subir | A porta já está ocupada por outra instância do servidor. Feche a anterior (ou troque a porta com `ASPNETCORE_URLS`). |
| `A network-related or instance-specific error` / timeout | O SQL Server não é alcançável: confira endereço, porta, firewall/VPN. |
| `Login failed for user` | Usuário/senha errados, ou o login não tem acesso ao banco. |
| `Cannot open database "ClanHub"` | O banco não existe: crie-o vazio (`CREATE DATABASE ClanHub;`). |
| `CREATE TABLE permission denied` | O login não pode criar tabelas; use um com permissão de dono (`db_owner`) para aplicar as migrations. |
| `certificate chain was issued by an authority that is not trusted` | Falta `TrustServerCertificate=True` na string (ou instale um certificado válido no servidor). |
| O link de confirmação não chega | Em desenvolvimento a confirmação está desligada (passo 6). Se ligou, sem SMTP o link só aparece no log do servidor. |
| "Muitas tentativas. Aguarde um instante." | Limite de requisições nas rotas de login/cadastro (20 por minuto por IP em produção; 200 em desenvolvimento). Espere 1 minuto. |
| `dotnet ef` não encontrado | Rode `dotnet tool restore` (passo 2). |
| `dotnet ef` diz que a string de conexão está vazia/localhost | Ele não lê user-secrets: defina `$env:ConnectionStrings__Default` (passo 4). |