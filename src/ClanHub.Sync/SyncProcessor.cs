using ClanHub.Application.Games;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Sync.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClanHub.Sync;

/// <summary>Executa um SyncTarget já reservado: chama o handler, reagenda e trata erros.</summary>
public class SyncProcessor(
    IServiceScopeFactory scopes, IOptions<SyncOptions> options, TimeProvider clock, ILogger<SyncProcessor> logger)
{
    private readonly SyncOptions _o = options.Value;

    private enum Outcome { Succeeded, Failed, Conflict }

    public async Task<bool> ProcessAsync(Guid targetId, CancellationToken ct)
    {
        // Clãs de workspaces diferentes compartilham jogadores (índice único por jogo+tag): se duas coletas
        // inserirem o mesmo jogador ao mesmo tempo, a perdedora simplesmente tenta de novo, já vendo o jogador.
        var outcome = await ProcessOnceAsync(targetId, retryOnConflict: true, ct);
        if (outcome == Outcome.Conflict) outcome = await ProcessOnceAsync(targetId, retryOnConflict: false, ct);
        return outcome != Outcome.Failed;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    private async Task<Outcome> ProcessOnceAsync(Guid targetId, bool retryOnConflict, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;

        var target = await db.SyncTargets.FirstOrDefaultAsync(t => t.Id == targetId, ct);
        if (target is null) return Outcome.Succeeded;

        var clan = await db.TrackedClans.FirstOrDefaultAsync(c => c.Id == target.TrackedClanId, ct);
        if (clan is null)
        {
            // Clã removido (exclusão lógica): a fila não precisa mais dele.
            db.SyncTargets.Remove(target);
            await db.SaveChangesAsync(ct);
            return Outcome.Succeeded;
        }

        var handler = scope.ServiceProvider.GetServices<ISyncHandler>().First(h => h.Kind == target.Kind);
        var connector = scope.ServiceProvider.GetRequiredService<IGameConnectorFactory>().Get(clan.Game);
        var ok = false;
        TimeSpan? pauseGame = null;

        try
        {
            var delay = await handler.HandleAsync(new SyncContext(db, clan, connector, now, _o), ct);
            target.NextRunAt = now + Scale(clan, delay, now);
            target.FailCount = 0;
            target.LastError = null;
            target.LastSuccessAt = now;
            ok = true;
        }
        catch (DbUpdateException ex) when (retryOnConflict && IsUniqueViolation(ex))
        {
            return Outcome.Conflict;
        }
        catch (SupercellException ex)
        {
            db.ChangeTracker.Clear();
            (target, clan) = await ReloadAsync(db, targetId, ct) ?? (target, clan);
            ok = false;
            pauseGame = ApplyError(target, clan, ex, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha inesperada na sincronização {Kind} do clã {Clan}", target.Kind, clan.Id);
            db.ChangeTracker.Clear();
            (target, clan) = await ReloadAsync(db, targetId, ct) ?? (target, clan);
            target.FailCount++;
            target.LastError = Truncate(ex.Message);
            target.NextRunAt = now + Backoff.For(target.FailCount, _o.MaxBackoffMinutes);
        }

        target.LastRunAt = now;
        target.LeaseUntil = null;
        target.LeaseOwner = null;
        await db.SaveChangesAsync(ct);

        if (pauseGame is { } pause)
        {
            var until = now + pause;
            var game = clan.Game;
            await db.SyncTargets
                .Where(t => db.TrackedClans.Any(c => c.Id == t.TrackedClanId && c.Game == game) && t.NextRunAt < until)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NextRunAt, until), ct);
        }
        return ok ? Outcome.Succeeded : Outcome.Failed;
    }

    private static async Task<(SyncTarget, TrackedClan)?> ReloadAsync(AppDbContext db, Guid targetId, CancellationToken ct)
    {
        var t = await db.SyncTargets.FirstOrDefaultAsync(x => x.Id == targetId, ct);
        if (t is null) return null;
        var c = await db.TrackedClans.FirstOrDefaultAsync(x => x.Id == t.TrackedClanId, ct);
        return c is null ? null : (t, c);
    }

    private TimeSpan? ApplyError(SyncTarget target, TrackedClan clan, SupercellException ex, DateTime now)
    {
        target.LastError = Truncate($"{ex.Kind}: {ex.Message}");
        switch (ex.Kind)
        {
            case SupercellErrorKind.Throttled:
                target.NextRunAt = now + (ex.RetryAfter ?? TimeSpan.FromMinutes(1));
                return null;
            case SupercellErrorKind.Maintenance:
                target.NextRunAt = now + TimeSpan.FromMinutes(_o.MaintenancePauseMinutes);
                return TimeSpan.FromMinutes(_o.MaintenancePauseMinutes);
            case SupercellErrorKind.PrivateWarLog:
                clan.Status = TrackedClanStatus.PrivateWarLog;
                target.NextRunAt = now + TimeSpan.FromMinutes(_o.PrivateWarLogRetryMinutes);
                return null;
            case SupercellErrorKind.NotFound:
                // 404 só significa "clã inexistente" nas coletas principais do clã; em rotas opcionais
                // (Capital, ranking, perfis...) é só "sem dados" e não pode marcar o clã inteiro.
                if (target.Kind is SyncKind.Members or SyncKind.CurrentWar or SyncKind.WarLog or SyncKind.RiverRace or SyncKind.RiverRaceLog)
                    clan.Status = TrackedClanStatus.NotFound;
                goto default;
            default:
                target.FailCount++;
                target.NextRunAt = now + Backoff.For(target.FailCount, _o.MaxBackoffMinutes);
                return null;
        }
    }

    /// <summary>Clãs sem acesso da equipe há muito tempo são sincronizados com menos frequência.</summary>
    private TimeSpan Scale(TrackedClan clan, TimeSpan delay, DateTime now)
    {
        var last = clan.LastAccessedAt ?? clan.CreatedAt;
        return now - last > TimeSpan.FromDays(_o.InactiveDays) ? delay * _o.InactiveMultiplier : delay;
    }

    private static string Truncate(string s) => s.Length <= 1000 ? s : s[..1000];
}