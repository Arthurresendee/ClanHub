using ClanHub.Application.Games;
using ClanHub.Domain;

namespace ClanHub.Application.Members;

public record KnownMember(string Tag, string Name, string Role, int? TownHall, int Donations);

public record DiffEvent(ClanEventType Type, string PlayerTag, object? Payload = null);

/// <summary>Compara o estado salvo com a resposta da Supercell e transforma diferenças em eventos.</summary>
public static class MemberDiffer
{
    public static IReadOnlyList<DiffEvent> Diff(
        IReadOnlyCollection<KnownMember> previous,
        IReadOnlyCollection<ClanMemberSnapshot> current,
        bool isFirstSync)
    {
        if (isFirstSync) return [];

        var events = new List<DiffEvent>();
        var before = previous.ToDictionary(m => m.Tag);
        var now = current.ToDictionary(m => m.Tag);

        foreach (var (tag, m) in now)
        {
            if (!before.TryGetValue(tag, out var old))
            {
                events.Add(new DiffEvent(ClanEventType.MemberJoined, tag, new { m.Name, m.Role }));
                continue;
            }
            if (old.Name != m.Name)
                events.Add(new DiffEvent(ClanEventType.NameChanged, tag, new { From = old.Name, To = m.Name }));
            if (old.Role != m.Role)
                events.Add(new DiffEvent(ClanEventType.RoleChanged, tag, new { From = old.Role, To = m.Role }));
            if (old.TownHall is { } oldTh && m.TownHall is { } newTh && newTh > oldTh)
                events.Add(new DiffEvent(ClanEventType.TownHallUpgraded, tag, new { From = oldTh, To = newTh }));
            // Queda de doações = virada de temporada (tratada em SeasonStats), não gera evento.
            if (m.Donations > old.Donations)
                events.Add(new DiffEvent(ClanEventType.DonationsChanged, tag,
                    new { From = old.Donations, To = m.Donations }));
        }

        foreach (var (tag, old) in before)
            if (!now.ContainsKey(tag))
                events.Add(new DiffEvent(ClanEventType.MemberLeft, tag, new { old.Name, old.Role }));

        return events;
    }

    /// <summary>Detecta virada de temporada: algum membro teve as doações zeradas ou reduzidas.</summary>
    public static bool DetectSeasonReset(
        IReadOnlyCollection<KnownMember> previous, IReadOnlyCollection<ClanMemberSnapshot> current)
    {
        var now = current.ToDictionary(m => m.Tag);
        return previous.Any(o => now.TryGetValue(o.Tag, out var m) && m.Donations < o.Donations);
    }
}