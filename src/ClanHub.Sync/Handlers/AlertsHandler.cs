using System.Globalization;
using System.Net.Http.Json;
using ClanHub.Application.Alerts;
using ClanHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClanHub.Sync.Handlers;

/// <summary>
/// Avalia as regras de alerta do clã (ataque pendente, saída/entrada, inatividade, decks pendentes)
/// e entrega no painel (Notification) e/ou em webhook do Discord. Cada situação dispara uma vez só.
/// </summary>
public class AlertsHandler(IHttpClientFactory http, ILogger<AlertsHandler> logger) : ISyncHandler
{
    public const string HttpClientName = "discord-webhook";

    public SyncKind Kind => SyncKind.Alerts;

    public async Task<TimeSpan> HandleAsync(SyncContext c, CancellationToken ct)
    {
        var delay = TimeSpan.FromMinutes(c.Options.AlertsMinutes);
        var db = c.Db;
        var rules = await db.AlertRules.Where(r => r.TrackedClanId == c.Clan.Id && r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return delay;

        var recipients = await (from m in db.WorkspaceMembers where m.WorkspaceId == c.Clan.WorkspaceId && m.Role >= WorkspaceRole.Staff
                                select m.UserId).ToListAsync(ct);

        foreach (var rule in rules)
        {
            var cfg = AlertConfig.Parse(rule.ConfigJson);
            var messages = new List<(string Title, string Body)>();
            string? key = null;
            long? newLastEventId = null; // só vira definitivo depois que a entrega der certo (senão o evento se perderia)

            switch (rule.Type)
            {
                case AlertTypes.MemberLeft:
                case AlertTypes.MemberJoined:
                {
                    var type = rule.Type == AlertTypes.MemberLeft ? ClanEventType.MemberLeft : ClanEventType.MemberJoined;
                    var events = await db.ClanEvents
                        .Where(e => e.TrackedClanId == c.Clan.Id && e.Type == type && e.Id > rule.LastEventId)
                        .OrderBy(e => e.Id).Take(20).ToListAsync(ct);
                    foreach (var e in events)
                        messages.Add((rule.Type == AlertTypes.MemberLeft ? "Saiu do clã" : "Entrou no clã",
                            $"{NameOf(e)} ({e.PlayerTag}) {(rule.Type == AlertTypes.MemberLeft ? "saiu de" : "entrou em")} {c.Clan.Name}."));
                    if (events.Count > 0) newLastEventId = events[^1].Id;
                    break;
                }
                case AlertTypes.PendingWarAttacks:
                {
                    var war = await db.Wars.Include(w => w.Participants)
                        .Where(w => w.TrackedClanId == c.Clan.Id && w.State == WarState.InWar && w.EndTime != null)
                        .OrderByDescending(w => w.StartTime).FirstOrDefaultAsync(ct);
                    if (war is null || war.EndTime is null) break;
                    var hoursLeft = (war.EndTime.Value - c.Now).TotalHours;
                    if (hoursLeft > (cfg.HoursBeforeEnd ?? 4) || hoursLeft < 0) break;
                    key = war.Id.ToString();
                    var pending = war.Participants.Where(p => p.AttacksUsed < war.AttacksPerMember).OrderBy(p => p.MapPosition).ToList();
                    if (pending.Count == 0) { key = null; break; }
                    messages.Add(("Ataques pendentes na guerra",
                        $"Faltam ~{Math.Max(1, (int)Math.Ceiling(hoursLeft))} h para o fim da guerra contra {war.OpponentName}. Sem atacar: {string.Join(", ", pending.Select(p => p.Name))}."));
                    break;
                }
                case AlertTypes.MemberInactive:
                {
                    var days = Math.Max(1, cfg.Days ?? 7);
                    var cutoff = c.Now.AddDays(-days);
                    var week = ISOWeek.GetWeekOfYear(c.Now);
                    key = $"{ISOWeek.GetYear(c.Now)}-W{week:00}";
                    var inactive = await db.ClanMemberships
                        .Where(m => m.TrackedClanId == c.Clan.Id && m.IsCurrent && (m.LastActivityAt ?? m.JoinedAt) < cutoff)
                        .OrderBy(m => m.LastActivityAt ?? m.JoinedAt).Select(m => m.Name).Take(30).ToListAsync(ct);
                    if (inactive.Count == 0) { key = null; break; }
                    messages.Add(("Membros inativos", $"Sem atividade há mais de {days} dias: {string.Join(", ", inactive)}."));
                    break;
                }
                case AlertTypes.RiverRaceDecksPending:
                {
                    if (!RiverRaceHandler.IsBattleDay(c.Now) || c.Now.Hour < (cfg.HourUtc ?? 8)) break;
                    var race = await db.RiverRaces.Include(r => r.Participants)
                        .FirstOrDefaultAsync(r => r.TrackedClanId == c.Clan.Id && r.IsCurrent, ct);
                    if (race is null) break;
                    key = c.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var pending = race.Participants.Where(p => p.DecksUsedToday < 4).OrderBy(p => p.DecksUsedToday).Select(p => $"{p.Name} ({p.DecksUsedToday}/4)").ToList();
                    if (pending.Count == 0) { key = null; break; }
                    messages.Add(("Decks pendentes hoje", $"Ainda sem os 4 decks: {string.Join(", ", pending)}."));
                    break;
                }
            }

            if (messages.Count == 0 || (key is not null && key == rule.LastFiredKey)) continue;

            var delivered = await DeliverAsync(c, rule, cfg, recipients, messages, ct);
            if (!delivered) continue;
            rule.LastFiredAt = c.Now;
            if (key is not null) rule.LastFiredKey = key;
            if (newLastEventId is { } lastId) rule.LastEventId = lastId;
        }

        await db.SaveChangesAsync(ct);
        return delay;
    }

    private static string NameOf(ClanEvent e)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(e.PayloadJson ?? "{}");
            return doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? e.PlayerTag ?? "Alguém" : e.PlayerTag ?? "Alguém";
        }
        catch { return e.PlayerTag ?? "Alguém"; }
    }

    private async Task<bool> DeliverAsync(SyncContext c, AlertRule rule, AlertConfig cfg, List<Guid> recipients,
        List<(string Title, string Body)> messages, CancellationToken ct)
    {
        if (rule.Channel == AlertChannel.Panel)
        {
            var link = $"/app/{c.Clan.WorkspaceId}/cla/{c.Clan.Id}";
            foreach (var (title, body) in messages)
                foreach (var user in recipients)
                    c.Db.Notifications.Add(new Notification
                    {
                        UserId = user, Title = $"{c.Clan.Name}: {title}", Body = body, Link = link, CreatedAt = c.Now,
                    });
            return true;
        }

        if (!AlertWebhook.IsValid(cfg.WebhookUrl))
        {
            logger.LogWarning("Regra {Rule} do clã {Clan} tem webhook inválido; ignorada.", rule.Id, c.Clan.Id);
            return false;
        }
        try
        {
            var content = string.Join("\n", messages.Select(m => $"**{c.Clan.Name} — {m.Title}**\n{m.Body}"));
            if (content.Length > 1900) content = content[..1900] + "…";
            var client = http.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(cfg.WebhookUrl, new { content, allowed_mentions = new { parse = Array.Empty<string>() } }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha ao chamar o webhook do Discord (regra {Rule}).", rule.Id);
            return false;
        }
    }
}