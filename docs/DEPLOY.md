# Deploy do ClanHub (Vercel + SQL Server na VPS)

## 1. Banco (uma vez)
- Porta do SQL Server aberta (preferir porta diferente de 1433), login `sa` desativado, `max server memory` configurado.
- Dois logins: `clanhub_migrator` (dono do banco, só migrations) e `clanhub_app` (`db_datareader` + `db_datawriter`).
- `Encrypt=True` sempre; ideal certificado válido (`TrustServerCertificate=False`).
- Backup diário fora da VPS, com restauração testada.

String de conexão (nunca no repositório): 
`Server=HOST,PORTA;Database=ClanHub;User Id=clanhub_app;Password=...;Encrypt=True;TrustServerCertificate=False;Max Pool Size=20;Connect Timeout=15`

## 2. Migrations
- Local: `dotnet ef database update --project src/ClanHub.Infrastructure --startup-project src/ClanHub.Infrastructure` com `ConnectionStrings__Default` (login migrator) no ambiente.
- Automático: o job `migrate` de `.github/workflows/ci.yml` usa o segredo `DB_MIGRATOR_CONNECTION`.

## 3. Vercel
1. New Project → importar o repositório. O `vercel.json` declara o serviço de contêiner (`Dockerfile.vercel`) e os crons.
2. Região: a mais próxima da VPS.
3. Variáveis de ambiente:
   `PORT=8080`, `ConnectionStrings__Default`, `CRON_SECRET`, `App__PublicUrl`, `Admin__Email`,
   `Supercell__ClashOfClans__ApiKey` / `__BaseUrl`, `Supercell__ClashRoyale__ApiKey` / `__BaseUrl`, `Supercell__BrawlStars__ApiKey` / `__BaseUrl`,
   `Email__Host`, `Email__Port`, `Email__User`, `Email__Password`, `Email__From`.
   `Auth__RequireConfirmedEmail` (padrão `true`; só desligue em ambiente de teste — enquanto o envio de e-mails não estiver implementado, o cadastro em produção depende dele).
   Opcionais: `Discord__ClientId` e `Discord__ClientSecret` (liga o botão "Entrar com Discord"; no portal do Discord cadastre o redirect `https://app.seudominio.com.br/api/auth/discord/signin`), `Redis__ConnectionString` (cache compartilhado entre instâncias), `RateLimit__AuthPerMinute`, `RateLimit__PublicPerMinute`.
4. Chaves da Supercell: cadastrar o IP `45.79.218.79` (proxy da RoyaleAPI); as BaseUrls padrão já apontam para o proxy.
5. Domínio: `app.seudominio.com.br`.
6. Cron:
   - **Pro**: os crons do `vercel.json` já valem (tick a cada minuto, manutenção às 06:00 UTC).
   - **Hobby**: o Vercel Cron só roda 1x por dia. Remova o cron do tick do `vercel.json` e use um cron externo (ex.: cron-job.org) chamando `GET https://app.../internal/sync/tick` a cada minuto com o cabeçalho `Authorization: Bearer <CRON_SECRET>`.
7. A duração máxima da função (configuração de duração máxima da função no projeto da Vercel; confira o campo suportado na documentação atual) deve ficar acima de `Sync:TickBudgetSeconds` (40 s).

## 4. Administrador da plataforma
Cadastre-se normalmente e defina `Admin__Email` com o seu e-mail: no próximo start o papel Admin é aplicado.

## 5. Antes de abrir ao público
Veja o checklist da seção "Riscos" do plano (nome/domínio sem marcas da Supercell, termos e privacidade revisados juridicamente, SPF/DKIM, backup testado, monitor em `/health`).