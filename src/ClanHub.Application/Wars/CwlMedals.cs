namespace ClanHub.Application.Wars;

public record CwlPlayerStats(string Tag, string Name, int RoundsPlayed, int Attacks, int Stars, double AverageDestruction);

public record CwlMedalSuggestion(
    string Tag, string Name, int RoundsPlayed, int Attacks, int Stars, double AverageDestruction,
    double Score, double SharePercent, int SuggestedMedals);

/// <summary>
/// Sugestão de distribuição das medalhas bônus da CWL. Pontua cada jogador por participação (rodadas jogadas),
/// estrelas e destruição média; quem não atacou nenhuma vez não recebe. É só uma sugestão: a liderança decide.
/// </summary>
public static class CwlMedals
{
    public static IReadOnlyList<CwlMedalSuggestion> Suggest(IReadOnlyCollection<CwlPlayerStats> players, int bonusPool)
    {
        var eligible = players.Where(p => p.Attacks > 0).ToList();
        if (eligible.Count == 0) return [];

        double Score(CwlPlayerStats p) => p.RoundsPlayed * 10 + p.Stars * 5 + p.AverageDestruction / 10.0;
        var total = eligible.Sum(Score);

        // Maior resto: distribui o pool inteiro sem sobrar nem faltar medalha.
        var raw = eligible.Select(p => (p, share: Score(p) / total)).ToList();
        var floors = raw.Select(r => (int)Math.Floor(r.share * Math.Max(0, bonusPool))).ToList();
        var left = Math.Max(0, bonusPool) - floors.Sum();
        var order = raw.Select((r, i) => (i, rest: r.share * bonusPool - floors[i])).OrderByDescending(x => x.rest).ThenBy(x => x.i).ToList();
        for (var k = 0; k < left; k++) floors[order[k % order.Count].i]++;

        return raw.Select((r, i) => new CwlMedalSuggestion(r.p.Tag, r.p.Name, r.p.RoundsPlayed, r.p.Attacks, r.p.Stars,
                Math.Round(r.p.AverageDestruction, 1), Math.Round(Score(r.p), 1), Math.Round(r.share * 100, 1), floors[i]))
            .OrderByDescending(s => s.Score).ThenBy(s => s.Name).ToList();
    }
}