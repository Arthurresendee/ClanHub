using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClanHub.Sync;

public record TickResult(int Leased, int Succeeded, int Failed, int Released, TimeSpan Elapsed);

/// <summary>Uma chamada do cron: reserva lotes de tarefas vencidas e processa até acabar o orçamento de tempo.</summary>
public class TickRunner(
    IServiceScopeFactory scopes, SyncProcessor processor, IOptions<SyncOptions> options)
{
    private readonly SyncOptions _o = options.Value;

    public async Task<TickResult> RunAsync(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(_o.TickBudgetSeconds);
        var owner = Guid.NewGuid().ToString("N");
        int leased = 0, ok = 0, failed = 0, released = 0;

        while (watch.Elapsed < budget && !ct.IsCancellationRequested)
        {
            IReadOnlyList<Guid> ids;
            await using (var scope = scopes.CreateAsyncScope())
                ids = await scope.ServiceProvider.GetRequiredService<SyncQueue>().LeaseAsync(owner, _o.BatchSize, ct);
            if (ids.Count == 0) break;
            leased += ids.Count;

            await Parallel.ForEachAsync(ids,
                new ParallelOptions { MaxDegreeOfParallelism = _o.MaxParallelism, CancellationToken = ct },
                async (id, token) =>
                {
                    if (watch.Elapsed >= budget)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<SyncQueue>().ReleaseAsync(id, CancellationToken.None);
                        Interlocked.Increment(ref released);
                        return;
                    }
                    if (await processor.ProcessAsync(id, token)) Interlocked.Increment(ref ok);
                    else Interlocked.Increment(ref failed);
                });
        }

        return new TickResult(leased, ok, failed, released, watch.Elapsed);
    }
}