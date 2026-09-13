namespace Awana.Scoring;

/// <summary>One team's line in a single round.</summary>
public sealed record RoundEntry(Guid TeamId, int? Place, decimal Points);

/// <summary>A round that counts. Voided rounds are never passed in.</summary>
public sealed record RoundTally(int RoundNumber, IReadOnlyList<RoundEntry> Entries);

/// <summary>Where a team stands, and how it got there.</summary>
/// <param name="Rank">
/// Standard competition ranking, so two teams level on 145 are both 2nd and the
/// next team is 4th.
/// </param>
/// <param name="RankChange">
/// Places gained since before the most recent round. Positive is upward. Zero
/// when nothing moved, and zero for everyone when there is only one round.
/// </param>
/// <param name="PlaceCounts">How many times this team finished 1st, 2nd, and so on.</param>
public sealed record TeamStanding(
    Guid TeamId,
    decimal Points,
    int Rank,
    int RankChange,
    IReadOnlyDictionary<int, int> PlaceCounts,
    decimal AdjustmentPoints);

/// <summary>
/// Turns rounds into standings.
///
/// This is the ONLY way a total is produced. Nothing is cached and no running
/// total is stored, so the number on the board cannot disagree with the rounds
/// behind it. At four teams and eight rounds this is thirty-two rows of
/// arithmetic, which is cheaper than serializing the answer.
/// </summary>
public static class StandingsCalculator
{
    public static IReadOnlyList<TeamStanding> Compute(
        IReadOnlyList<Guid> teamIds,
        IReadOnlyList<RoundTally> rounds,
        IReadOnlyDictionary<Guid, decimal>? adjustments = null)
    {
        ArgumentNullException.ThrowIfNull(teamIds);
        ArgumentNullException.ThrowIfNull(rounds);

        var ordered = rounds.OrderBy(r => r.RoundNumber).ToList();

        var totals = Totals(teamIds, ordered, adjustments);

        // Ranks as they stood before the most recent round, so the board can
        // show who moved. With fewer than two rounds nobody has moved yet.
        var previousRanks = ordered.Count > 1
            ? Ranks(Totals(teamIds, ordered.Take(ordered.Count - 1).ToList(), adjustments))
            : null;

        var ranks = Ranks(totals);
        var placeCounts = PlaceCounts(teamIds, ordered);

        return teamIds
            .Select(id => new TeamStanding(
                TeamId: id,
                Points: totals[id],
                Rank: ranks[id],
                RankChange: previousRanks is null ? 0 : previousRanks[id] - ranks[id],
                PlaceCounts: placeCounts[id],
                AdjustmentPoints: adjustments is not null && adjustments.TryGetValue(id, out var adj) ? adj : 0m))
            .OrderBy(s => s.Rank)
            .ThenBy(s => s.TeamId)
            .ToList();
    }

    private static Dictionary<Guid, decimal> Totals(
        IReadOnlyList<Guid> teamIds,
        IReadOnlyList<RoundTally> rounds,
        IReadOnlyDictionary<Guid, decimal>? adjustments)
    {
        // Every team starts at zero, including one that has not played a round
        // yet. A team missing from the output is worse than a team on nothing.
        var totals = teamIds.ToDictionary(id => id, _ => 0m);

        foreach (var entry in rounds.SelectMany(r => r.Entries))
        {
            if (totals.ContainsKey(entry.TeamId)) totals[entry.TeamId] += entry.Points;
        }

        if (adjustments is not null)
        {
            foreach (var (teamId, points) in adjustments)
            {
                if (totals.ContainsKey(teamId)) totals[teamId] += points;
            }
        }

        return totals;
    }

    /// <summary>Standard competition ranking: 1, 2, 2, 4.</summary>
    private static Dictionary<Guid, int> Ranks(Dictionary<Guid, decimal> totals)
    {
        var ranks = new Dictionary<Guid, int>(totals.Count);
        var position = 0;
        decimal? previousPoints = null;
        var previousRank = 0;

        foreach (var (teamId, points) in totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key))
        {
            position++;

            if (previousPoints is not null && points == previousPoints)
            {
                ranks[teamId] = previousRank;
                continue;
            }

            ranks[teamId] = position;
            previousRank = position;
            previousPoints = points;
        }

        return ranks;
    }

    private static Dictionary<Guid, IReadOnlyDictionary<int, int>> PlaceCounts(
        IReadOnlyList<Guid> teamIds, IReadOnlyList<RoundTally> rounds)
    {
        var counts = teamIds.ToDictionary(id => id, _ => new Dictionary<int, int>());

        foreach (var entry in rounds.SelectMany(r => r.Entries))
        {
            if (entry.Place is not { } place) continue;
            if (!counts.TryGetValue(entry.TeamId, out var forTeam)) continue;

            forTeam[place] = forTeam.GetValueOrDefault(place) + 1;
        }

        return counts.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<int, int>)kv.Value);
    }
}
