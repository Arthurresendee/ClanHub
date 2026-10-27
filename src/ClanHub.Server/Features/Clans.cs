using ClanHub.Application.Clans;
using ClanHub.Application.Games;
using ClanHub.Contracts;
using ClanHub.Domain;
using ClanHub.Infrastructure.Persistence;
using ClanHub.Server.Common;
using ClanHub.Sync;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClanHub.Server.Features;

public static class ClanEndpoints
{
    public static void MapClans(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/workspaces/{wsId:guid}/clans", async (Guid wsId, AppDbContext db) =>
            Results.Ok(await Queries.SummariesAsync(db, db.TrackedClans.Where(c => c.WorkspaceId == wsId))))
            .RequireWorkspaceRole(WorkspaceRole.Viewer);

        var c = app.MapGroup("/api/v1/clans/{clanId:guid}");
        c.MapGet("/", Overview).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapPatch("/", Update).RequireWorkspaceRole(WorkspaceRole.Admin);
        c.MapGet("/settings", GetSettings).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/seasons", Seasons).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapDelete("/", Delete).RequireWorkspaceRole(WorkspaceRole.Admin);
        c.MapPost("/refresh", Refresh).RequireWorkspaceRole(WorkspaceRole.Staff);
        c.MapGet("/sync-status", SyncStatus).RequireWorkspaceRole(WorkspaceRole.Viewer);

        c.MapGet("/members", Members).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/members/{playerTag}", MemberDetail).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/members/{playerTag}/history", History).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/events", Events).RequireWorkspaceRole(WorkspaceRole.Viewer);
        c.MapGet("/seasons/{season}/stats", SeasonStats).RequireWorkspaceRole(WorkspaceRole.Viewer);

        c.MapGet("/members/{playerTag}/notes", ListNotes).RequireWorkspaceRole(WorkspaceRole.Staff);
        c.MapPost("/members/{playerTag}/notes", CreateNote).RequireWorkspaceRole(WorkspaceRole.Staff);
        var n = app.MapGroup("/api/v1/notes/{noteId:guid}");
        n.MapPatch("/", UpdateNote).RequireWorkspaceRole(WorkspaceRole.Staff);
        n.MapDelete("/", DeleteNote).RequireWorkspaceRole(WorkspaceRole.Staff);
    }

    private static EventDto ToDto(ClanEvent e) => new(e.Id, e.Type.ToString(), e.PlayerTag, e.PayloadJson, e.OccurredAt);

    private static async Task<IResult> Overview(Guid clanId, AppDbContext db, IGameConnectorFactory games, CancellationToken ct)
    {
        var clan = await db.TrackedClans.FirstOrDefaultAsync(x => x.Id == clanId, ct);
        if (clan is null) return Results2.NotFound("Clã não encontrado.");
        var summary = (await Queries.SummariesAsync(db, db.TrackedClans.Where(x => x.Id == clanId), ct))[0];

        var totals = await db.ClanMemberships.Where(m => m.TrackedClanId == clanId && m.IsCurrent)
            .GroupBy(_ => 1).Select(g => new { Donations = g.Sum(m => m.Donations), Trophies = g.Sum(m => m.Trophies) })
            .FirstOrDefaultAsync(ct);

        WarBriefDto? warBrief = null;
        var war = await db.Wars.Include(w => w.Participants)
            .Where(w => w.TrackedClanId == clanId && w.Type == WarType.Normal && (w.State == WarState.InWar || w.State == WarState.Preparation))
            .OrderByDescending(w => w.StartTime).FirstOrDefaultAsync(ct);
        if (war is not null)
            warBrief = new WarBriefDto(war.State.ToString(), war.OpponentName, war.TeamSize, war.Stars, war.OpponentStars, war.EndTime,
                war.Participants.Count(p => p.AttacksUsed < war.AttacksPerMember));

        RiverRaceBriefDto? raceBrief = null;
        var race = await db.RiverRaces.Include(r => r.Participants).FirstOrDefaultAsync(r => r.TrackedClanId == clanId && r.IsCurrent, ct);
        if (race is not null)
            raceBrief = new RiverRaceBriefDto(race.Fame, race.Rank, race.Participants.Count(p => p.DecksUsedToday < 4));

        var events = await db.ClanEvents.Where(e => e.TrackedClanId == clanId).OrderByDescending(e => e.Id).Take(20).ToListAsync(ct);
        var lastId = events.Count > 0 ? events[0].Id : 0;

        return Results.Ok(new ClanOverviewDto(summary, GameMap.Dto(games.Get(clan.Game).Features),
            totals?.Donations ?? 0, totals?.Trophies ?? 0, warBrief, raceBrief, lastId, events.Select(ToDto).ToList()));
    }

    private static ClanSettingsDto ToDto(TrackedClan clan)
    {
        var s = ClanSettings.Parse(clan.SettingsJson);
        return new ClanSettingsDto(s.CountryCode, s.StrikeExpiryDays, s.StrikesToWarn, clan.IsPublic, clan.PublicSlug, clan.RecruitmentText);
    }

    private static async Task<IResult> GetSettings(Guid clanId, AppDbContext db) =>
        Results.Ok(ToDto(await db.TrackedClans.FirstAsync(x => x.Id == clanId)));

    private static readonly System.Text.RegularExpressions.Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{1,58})[a-z0-9]$");

    private static async Task<IResult> Update(Guid clanId, UpdateClanRequest req, HttpContext http, AppDbContext db)
    {
        var clan = await db.TrackedClans.FirstAsync(x => x.Id == clanId);
        var s = ClanSettings.Parse(clan.SettingsJson);

        if (req.StrikeExpiryDays is < 1 or > 365) return Results2.BadRequest("A validade dos strikes deve ficar entre 1 e 365 dias.");
        if (req.StrikesToWarn is < 1 or > 20) return Results2.BadRequest("O limite de strikes deve ficar entre 1 e 20.");
        if (req.CountryCode is { Length: > 0 } cc && !System.Text.RegularExpressions.Regex.IsMatch(cc, "^[A-Za-z]{2,6}$|^global$"))
            return Results2.BadRequest("Código de país inválido.");

        if (req.PublicSlug is { } slug)
        {
            slug = slug.Trim().ToLowerInvariant();
            if (!SlugPattern.IsMatch(slug)) return Results2.BadRequest("O endereço público deve ter 3 a 60 letras minúsculas, números ou hífens.");
            if (await db.TrackedClans.AnyAsync(c => c.PublicSlug == slug && c.Id != clanId))
                return Results2.Conflict("Este endereço público já está em uso.");
            clan.PublicSlug = slug;
        }
        if (req.IsPublic is { } pub)
        {
            if (pub && string.IsNullOrEmpty(clan.PublicSlug)) return Results2.BadRequest("Defina um endereço público antes de ativar a página.");
            clan.IsPublic = pub;
        }
        if (req.RecruitmentText is { } text) clan.RecruitmentText = string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, 2000)];

        s = s with
        {
            CountryCode = req.CountryCode is null ? s.CountryCode : (req.CountryCode.Length == 0 ? null : req.CountryCode.ToLowerInvariant()),
            StrikeExpiryDays = req.StrikeExpiryDays ?? s.StrikeExpiryDays,
            StrikesToWarn = req.StrikesToWarn ?? s.StrikesToWarn,
        };
        clan.SettingsJson = s.ToJson();

        Queries.Audit(db, clan.WorkspaceId, http.User.UserId(), "clan.updated", new { clan.Tag });
        await db.SaveChangesAsync();
        return Results.Ok(ToDto(clan));
    }

    private static async Task<IResult> Delete(Guid clanId, HttpContext http, AppDbContext db)
    {
        var clan = await db.TrackedClans.FirstAsync(x => x.Id == clanId);
        clan.IsDeleted = true;
        await db.SyncTargets.Where(t => t.TrackedClanId == clanId).ExecuteDeleteAsync();
        Queries.Audit(db, clan.WorkspaceId, http.User.UserId(), "clan.removed", new { clan.Tag });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Atualizar agora: só antecipa o NextRunAt, com intervalo mínimo por clã.</summary>
    private static async Task<IResult> Refresh(Guid clanId, AppDbContext db, TimeProvider clock, IOptions<SyncOptions> options)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var min = TimeSpan.FromSeconds(options.Value.ManualRefreshMinIntervalSeconds);
        var last = await db.SyncTargets.Where(t => t.TrackedClanId == clanId).MaxAsync(t => t.LastRunAt);
        if (last is { } l && now - l < min)
        {
            var wait = (int)Math.Ceiling((min - (now - l)).TotalSeconds);
            return Results.Json(new RefreshResponse(0, wait), statusCode: StatusCodes.Status429TooManyRequests);
        }
        var queued = await db.SyncTargets.Where(t => t.TrackedClanId == clanId && t.NextRunAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.NextRunAt, now));
        return Results.Accepted(value: new RefreshResponse(queued, 0));
    }

    private static async Task<IResult> SyncStatus(Guid clanId, AppDbContext db)
    {
        var clan = await db.TrackedClans.FirstAsync(x => x.Id == clanId);
        var targets = await db.SyncTargets.Where(t => t.TrackedClanId == clanId).OrderBy(t => t.Kind)
            .Select(t => new SyncStatusItemDto(t.Kind.ToString(), t.NextRunAt, t.LastRunAt, t.LastSuccessAt, t.FailCount, t.LastError)).ToListAsync();
        return Results.Ok(new SyncStatusDto(clan.Status.ToString(), targets));
    }

    private static async Task<IResult> Members(Guid clanId, AppDbContext db, string? status, string? sort, string? q)
    {
        var query = from m in db.ClanMemberships join p in db.Players on m.PlayerId equals p.Id
                    where m.TrackedClanId == clanId select new { m, p.Tag };
        query = (status?.ToLowerInvariant()) switch
        {
            "former" => query.Where(x => !x.m.IsCurrent),
            "all" => query,
            _ => query.Where(x => x.m.IsCurrent),
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.m.Name.Contains(term) || x.Tag.Contains(term));
        }
        query = (sort?.ToLowerInvariant()) switch
        {
            "name" => query.OrderBy(x => x.m.Name),
            "trophies" => query.OrderByDescending(x => x.m.Trophies),
            "donations" => query.OrderByDescending(x => x.m.Donations),
            "level" => query.OrderByDescending(x => x.m.ExpLevel),
            "activity" => query.OrderByDescending(x => x.m.LastActivityAt),
            _ => query.OrderBy(x => x.m.ClanRank).ThenBy(x => x.m.Name),
        };
        var rows = await query.Take(500).ToListAsync();
        var strikes = await StrikesAsync(db, clanId);
        return Results.Ok(rows.Select(x => ToMember(x.m, x.Tag, strikes)).ToList());
    }

    private static async Task<Dictionary<string, int>> StrikesAsync(AppDbContext db, Guid clanId)
    {
        var now = DateTime.UtcNow;
        return await db.MemberNotes
            .Where(n => n.TrackedClanId == clanId && n.Kind == NoteKind.Strike && (n.ExpiresAt == null || n.ExpiresAt > now))
            .GroupBy(n => n.PlayerTag).Select(g => new { g.Key, P = g.Sum(n => n.Points) })
            .ToDictionaryAsync(x => x.Key, x => x.P);
    }

    private static MemberDto ToMember(ClanMembership m, string tag, Dictionary<string, int> strikes) =>
        new(tag, m.Name, m.Role, m.ExpLevel, m.Trophies, m.Donations, m.DonationsReceived, m.TownHall, m.ClanRank,
            m.IsCurrent, m.JoinedAt, m.LeftAt, m.LastActivityAt, strikes.GetValueOrDefault(tag));

    private static async Task<IResult> MemberDetail(Guid clanId, string playerTag, AppDbContext db)
    {
        var tag = Tags.Normalize(playerTag);
        var row = await (from m in db.ClanMemberships join p in db.Players on m.PlayerId equals p.Id
                         where m.TrackedClanId == clanId && p.Tag == tag select new { m, p }).FirstOrDefaultAsync();
        if (row is null) return Results2.NotFound("Membro não encontrado.");

        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var history = await db.MemberDailySnapshots.Where(s => s.TrackedClanId == clanId && s.PlayerId == row.p.Id && s.Date >= from)
            .OrderBy(s => s.Date).Select(s => new DailyPointDto(s.Date, s.Trophies, s.Donations, s.Received, s.ExpLevel, s.TownHall)).ToListAsync();

        var wars = await (from wp in db.WarParticipants join w in db.Wars on wp.WarId equals w.Id
                          where wp.PlayerTag == tag && w.TrackedClanId == clanId orderby w.EndTime descending
                          select w).Take(20).ToListAsync();

        var strikes = await StrikesAsync(db, clanId);
        return Results.Ok(new MemberDetailDto(ToMember(row.m, tag, strikes), row.p.ProfileJson, history,
            wars.Select(WarEndpoints.ToSummary).ToList()));
    }

    private static async Task<IResult> History(Guid clanId, string playerTag, AppDbContext db, DateOnly? from, DateOnly? to)
    {
        var tag = Tags.Normalize(playerTag);
        var playerId = await db.Players.Where(p => p.Tag == tag).Select(p => (Guid?)p.Id).ToListAsync();
        var lo = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var hi = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var ids = playerId.Where(i => i.HasValue).Select(i => i!.Value).ToList();
        var points = await db.MemberDailySnapshots
            .Where(s => s.TrackedClanId == clanId && ids.Contains(s.PlayerId) && s.Date >= lo && s.Date <= hi)
            .OrderBy(s => s.Date).Select(s => new DailyPointDto(s.Date, s.Trophies, s.Donations, s.Received, s.ExpLevel, s.TownHall)).ToListAsync();
        return Results.Ok(points);
    }

    /// <summary>Feed de atividade. Com afterId devolve só os eventos novos (polling a cada 20 s).</summary>
    private static async Task<IResult> Events(Guid clanId, AppDbContext db, string? type, string? playerTag,
        string? cursor, long? afterId, int? limit)
    {
        var q = db.ClanEvents.Where(e => e.TrackedClanId == clanId);
        if (Enum.TryParse<ClanEventType>(type, true, out var t)) q = q.Where(e => e.Type == t);
        if (!string.IsNullOrWhiteSpace(playerTag)) { var tag = Tags.Normalize(playerTag); q = q.Where(e => e.PlayerTag == tag); }
        var take = Math.Clamp(limit ?? 50, 1, 200);

        if (afterId is { } after)
        {
            var fresh = await q.Where(e => e.Id > after).OrderBy(e => e.Id).Take(take).ToListAsync();
            return Results.Ok(new EventsResponse(fresh.Select(ToDto).ToList(), fresh.Count > 0 ? fresh[^1].Id : after, null));
        }

        if (long.TryParse(cursor, out var c)) q = q.Where(e => e.Id < c);
        var rows = await q.OrderByDescending(e => e.Id).Take(take + 1).ToListAsync();
        var items = rows.Take(take).ToList();
        var lastId = items.Count > 0 && cursor is null
            ? items[0].Id
            : await db.ClanEvents.Where(e => e.TrackedClanId == clanId).Select(e => (long?)e.Id).MaxAsync() ?? 0;
        return Results.Ok(new EventsResponse(items.Select(ToDto).ToList(), lastId, rows.Count > take ? items[^1].Id.ToString() : null));
    }

    private static async Task<IResult> Seasons(Guid clanId, AppDbContext db)
    {
        var closed = await db.SeasonStats.Where(s => s.TrackedClanId == clanId).Select(s => s.Season).Distinct()
            .OrderByDescending(s => s).ToListAsync();
        return Results.Ok(new SeasonListDto(["current", .. closed]));
    }

    private static async Task<IResult> SeasonStats(Guid clanId, string season, AppDbContext db)
    {
        if (season.Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            var live = await (from m in db.ClanMemberships join p in db.Players on m.PlayerId equals p.Id
                              where m.TrackedClanId == clanId && m.IsCurrent orderby m.Donations descending
                              select new SeasonStatRowDto(p.Tag, m.Name, m.Donations, m.DonationsReceived, 0, 0, 0)).ToListAsync();
            return Results.Ok(live);
        }
        var rows = await (from s in db.SeasonStats join p in db.Players on s.PlayerId equals p.Id
                          where s.TrackedClanId == clanId && s.Season == season orderby s.Donations descending
                          select new SeasonStatRowDto(p.Tag, p.Name, s.Donations, s.Received, s.WarStars, s.AttacksUsed, s.AttacksMissed)).ToListAsync();
        return Results.Ok(rows);
    }

    // ---- Notas ----
    private static async Task<IResult> ListNotes(Guid clanId, string playerTag, AppDbContext db)
    {
        var tag = Tags.Normalize(playerTag);
        var notes = await (from n in db.MemberNotes join u in db.Users on n.AuthorUserId equals u.Id
                           where n.TrackedClanId == clanId && n.PlayerTag == tag orderby n.CreatedAt descending
                           select new NoteDto(n.Id, n.PlayerTag, n.AuthorUserId, u.DisplayName, n.Kind.ToString(), n.Text, n.Points, n.ExpiresAt, n.CreatedAt)).ToListAsync();
        return Results.Ok(notes);
    }

    private static async Task<IResult> CreateNote(Guid clanId, string playerTag, CreateNoteRequest req, HttpContext http, AppDbContext db,
        UserManager<ApplicationUser> users)
    {
        if (!Enum.TryParse<NoteKind>(req.Kind, true, out var kind) || !Enum.IsDefined(kind)) return Results2.BadRequest("Tipo inválido.");
        var text = req.Text?.Trim() ?? "";
        if (text.Length is 0 or > 2000) return Results2.BadRequest("O texto deve ter entre 1 e 2000 caracteres.");

        var uid = http.User.UserId();
        var note = new MemberNote
        {
            TrackedClanId = clanId, PlayerTag = Tags.Normalize(playerTag), AuthorUserId = uid, Kind = kind, Text = text,
            Points = kind == NoteKind.Strike ? Math.Max(1, req.Points) : 0,
            ExpiresAt = req.ExpiresAt ?? (kind == NoteKind.Strike
                ? DateTime.UtcNow.AddDays(ClanSettings.Parse((await db.TrackedClans.Where(c => c.Id == clanId).Select(c => c.SettingsJson).FirstOrDefaultAsync())).StrikeExpiryDays)
                : null),
        };
        db.MemberNotes.Add(note);
        await db.SaveChangesAsync();
        var author = (await users.FindByIdAsync(uid.ToString()))?.DisplayName ?? "";
        return Results.Created($"/api/v1/notes/{note.Id}",
            new NoteDto(note.Id, note.PlayerTag, uid, author, kind.ToString(), note.Text, note.Points, note.ExpiresAt, note.CreatedAt));
    }

    private static async Task<IResult> UpdateNote(Guid noteId, UpdateNoteRequest req, HttpContext http, AppDbContext db)
    {
        var note = await db.MemberNotes.FirstAsync(n => n.Id == noteId);
        if (!CanChange(http, note)) return Results2.Problem(403, "Só o autor ou um administrador pode editar.");
        if (req.Text is { } t) note.Text = t.Trim();
        if (req.Points is { } p && note.Kind == NoteKind.Strike) note.Points = Math.Max(1, p);
        if (req.ExpiresAt is { } e) note.ExpiresAt = e;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteNote(Guid noteId, HttpContext http, AppDbContext db)
    {
        var note = await db.MemberNotes.FirstAsync(n => n.Id == noteId);
        if (!CanChange(http, note)) return Results2.Problem(403, "Só o autor ou um administrador pode remover.");
        db.MemberNotes.Remove(note);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static bool CanChange(HttpContext http, MemberNote note) =>
        note.AuthorUserId == http.User.UserId() || http.CurrentRole() >= WorkspaceRole.Admin;
}