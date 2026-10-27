using System.Data;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace ClanHub.Sync;

/// <summary>Fila de sincronização no SQL Server com reserva (lease) atômica.</summary>
public class SyncQueue(AppDbContext db, IOptions<SyncOptions> options)
{
    // CTE com TOP + ORDER BY, pois UPDATE TOP não aceita ORDER BY. READPAST evita pegar linhas travadas por outra chamada.
    private const string LeaseSql = """
        WITH due AS (
            SELECT TOP (@n) *
            FROM [sync].[SyncTarget] WITH (ROWLOCK, UPDLOCK, READPAST)
            WHERE NextRunAt <= SYSUTCDATETIME()
              AND (LeaseUntil IS NULL OR LeaseUntil < SYSUTCDATETIME())
            ORDER BY NextRunAt)
        UPDATE due
        SET LeaseUntil = DATEADD(minute, @lease, SYSUTCDATETIME()), LeaseOwner = @owner
        OUTPUT inserted.Id;
        """;

    public async Task<IReadOnlyList<Guid>> LeaseAsync(string owner, int count, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = LeaseSql;
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            Add(cmd, "@n", DbType.Int32, count);
            Add(cmd, "@lease", DbType.Int32, options.Value.LeaseMinutes);
            Add(cmd, "@owner", DbType.String, owner);

            var ids = new List<Guid>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
            return ids;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task ReleaseAsync(Guid id, CancellationToken ct) =>
        db.SyncTargets.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LeaseUntil, (DateTime?)null)
                .SetProperty(t => t.LeaseOwner, (string?)null), ct);

    private static void Add(System.Data.Common.DbCommand cmd, string name, DbType type, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}