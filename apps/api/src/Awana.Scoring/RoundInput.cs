namespace Awana.Scoring;

/// <summary>
/// One team's result in a single round.
/// </summary>
/// <param name="TeamId">The team. The engine never looks this up, it only carries it through.</param>
/// <param name="Place">
/// Where the team finished, 1 being first. Teams that finished level share the
/// same value. Null means the team did not take part, which is different from
/// finishing last: an absent team consumes no slot and shifts nobody.
/// Places need not be contiguous. 1, 2, 4 scores identically to 1, 2, 3.
/// </param>
/// <param name="IsDisqualified">Handled according to <see cref="ScoringConfig.DqRule"/>.</param>
/// <param name="Bonus">
/// Extra points for this team in this round, added after the round multiplier
/// so that a double round never doubles a bonus.
/// </param>
/// <param name="DisplayName">
/// Optional, and used only to write a readable explanation, for example naming
/// who a team tied with. The engine has no other notion of team identity and
/// works correctly without it.
/// </param>
public sealed record TeamEntry(
    Guid TeamId,
    int? Place,
    bool IsDisqualified = false,
    decimal Bonus = 0m,
    string? DisplayName = null);

/// <summary>
/// A complete round, ready to score.
/// </summary>
/// <param name="Entries">Every team being scored. Order is irrelevant, only <see cref="TeamEntry.Place"/> matters.</param>
/// <param name="Multiplier">
/// Applied to placement points only, for a final round or a tug of war worth
/// double. Bonuses are excluded deliberately.
/// </param>
public sealed record RoundInput(IReadOnlyList<TeamEntry> Entries, decimal Multiplier = 1.0m)
{
    /// <summary>
    /// Builds a round from the shape the scorekeeper console actually produces:
    /// an ordered list of place groups, where each inner list is a set of teams
    /// that finished level.
    /// </summary>
    public static RoundInput FromPlaceGroups(
        IReadOnlyList<IReadOnlyList<Guid>> orderedGroups,
        IReadOnlySet<Guid>? disqualified = null,
        IReadOnlyList<Guid>? absent = null,
        decimal multiplier = 1.0m)
    {
        var entries = new List<TeamEntry>();

        for (var i = 0; i < orderedGroups.Count; i++)
        {
            foreach (var teamId in orderedGroups[i])
            {
                entries.Add(new TeamEntry(
                    teamId,
                    Place: i + 1,
                    IsDisqualified: disqualified?.Contains(teamId) ?? false));
            }
        }

        foreach (var teamId in absent ?? [])
        {
            entries.Add(new TeamEntry(teamId, Place: null));
        }

        return new RoundInput(entries, multiplier);
    }
}
