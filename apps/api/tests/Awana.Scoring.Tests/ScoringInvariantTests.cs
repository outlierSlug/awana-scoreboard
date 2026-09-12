namespace Awana.Scoring.Tests;

/// <summary>
/// Properties that must hold for every input, rather than for one worked
/// example. These catch the class of bug that a table of cases walks straight
/// past, such as a result that quietly depends on the order teams were listed in.
/// </summary>
public class ScoringInvariantTests
{
    private static readonly ScoringEngine Engine = new();

    /// <summary>Every shape of round worth checking a property against.</summary>
    public static TheoryData<int?[]> PlacePatterns =>
    [
        [1, 2, 3, 4],
        [1, 1, 3, 4],
        [1, 2, 2, 4],
        [1, 2, 3, 3],
        [1, 1, 3, 3],
        [1, 1, 1, 4],
        [1, 2, 2, 2],
        [1, 1, 1, 1],
        [1, 2, 3, null],
        [1, 1, null, null],
        [1, 2, 4, 7],
    ];

    private static List<TeamEntry> EntriesFor(int?[] places) =>
        Teams.Four.Select((id, i) => new TeamEntry(id, places[i])).ToList();

    [Theory]
    [MemberData(nameof(PlacePatterns))]
    public void Shuffling_the_input_never_changes_the_result(int?[] places)
    {
        var entries = EntriesFor(places);

        var expected = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        // Every permutation of four teams, so this is exhaustive rather than
        // a sample.
        foreach (var permutation in Permutations(entries))
        {
            var actual = Engine.Score(new RoundInput(permutation), ScoringConfig.Default);

            Assert.Equal(expected.Awards.Count, actual.Awards.Count);

            foreach (var award in expected.Awards)
            {
                Assert.Equal(award.Points, actual.PointsFor(award.TeamId));
                Assert.Equal(award.Place, actual.AwardFor(award.TeamId).Place);
            }
        }
    }

    [Theory]
    [MemberData(nameof(PlacePatterns))]
    public void Points_awarded_equal_the_value_of_the_slots_consumed(int?[] places)
    {
        var entries = EntriesFor(places);
        var outcome = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        var slotsConsumed = places.Count(p => p is not null);
        var expected = ScoringConfig.Default.PlacePoints.Take(slotsConsumed).Sum();

        // Splitting three ways can leave a fraction of a penny on the table, so
        // this allows a cent per team rather than demanding exactness.
        var tolerance = 0.01m * entries.Count;

        Assert.InRange(outcome.TotalAwarded, expected - tolerance, expected + tolerance);
    }

    [Theory]
    [MemberData(nameof(PlacePatterns))]
    public void Every_team_appears_exactly_once_with_none_invented(int?[] places)
    {
        var entries = EntriesFor(places);
        var outcome = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        Assert.Equal(entries.Count, outcome.Awards.Count);
        Assert.Equal(
            entries.Select(e => e.TeamId).OrderBy(id => id),
            outcome.Awards.Select(a => a.TeamId).OrderBy(id => id));
    }

    [Theory]
    [MemberData(nameof(PlacePatterns))]
    public void Every_award_carries_an_explanation(int?[] places)
    {
        var entries = EntriesFor(places);
        var outcome = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        Assert.All(outcome.Awards, a => Assert.False(string.IsNullOrWhiteSpace(a.Explanation)));
    }

    [Theory]
    [MemberData(nameof(PlacePatterns))]
    public void A_disqualification_never_increases_anyone_elses_score(int?[] places)
    {
        var entries = EntriesFor(places);
        var clean = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        // Disqualify each team in turn and confirm nobody else gains. This is
        // the property the "share is forfeited, not redistributed" rule exists
        // to protect, stated directly.
        foreach (var victim in Teams.Four)
        {
            var withDq = entries
                .Select(e => e.TeamId == victim ? e with { IsDisqualified = true } : e)
                .ToList();

            var outcome = Engine.Score(new RoundInput(withDq), ScoringConfig.Default);

            foreach (var other in Teams.Four.Where(t => t != victim))
            {
                Assert.True(
                    outcome.PointsFor(other) <= clean.PointsFor(other),
                    $"Disqualifying {victim} increased the score of {other} " +
                    $"from {clean.PointsFor(other)} to {outcome.PointsFor(other)}.");
            }
        }
    }

    private static IEnumerable<List<TeamEntry>> Permutations(List<TeamEntry> source)
    {
        if (source.Count <= 1)
        {
            yield return source;
            yield break;
        }

        for (var i = 0; i < source.Count; i++)
        {
            var rest = source.Where((_, index) => index != i).ToList();

            foreach (var tail in Permutations(rest))
            {
                yield return [source[i], .. tail];
            }
        }
    }
}
