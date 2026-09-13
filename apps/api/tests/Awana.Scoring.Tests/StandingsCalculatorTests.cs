namespace Awana.Scoring.Tests;

public class StandingsCalculatorTests
{
    private static readonly Guid[] All = Teams.Four;

    private static RoundTally Round(int number, params (Guid Team, int? Place, decimal Points)[] entries) =>
        new(number, entries.Select(e => new RoundEntry(e.Team, e.Place, e.Points)).ToList());

    private static TeamStanding For(IReadOnlyList<TeamStanding> standings, Guid teamId) =>
        standings.Single(s => s.TeamId == teamId);

    [Fact]
    public void A_team_that_has_not_played_still_appears_on_zero()
    {
        // A missing row on a scoreboard reads as a bug. Zero reads as a score.
        var standings = StandingsCalculator.Compute(All, []);

        Assert.Equal(4, standings.Count);
        Assert.All(standings, s => Assert.Equal(0m, s.Points));
    }

    [Fact]
    public void Points_accumulate_across_rounds()
    {
        var standings = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 3, 20), (Teams.Green, 4, 10)),
            Round(2, (Teams.Red, 4, 10), (Teams.Blue, 1, 40), (Teams.Yellow, 2, 30), (Teams.Green, 3, 20)),
        ]);

        Assert.Equal(70m, For(standings, Teams.Blue).Points);
        Assert.Equal(50m, For(standings, Teams.Red).Points);
        Assert.Equal(50m, For(standings, Teams.Yellow).Points);
        Assert.Equal(30m, For(standings, Teams.Green).Points);
    }

    [Fact]
    public void Teams_level_on_points_share_a_rank_and_the_next_team_skips_one()
    {
        var standings = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 2, 30), (Teams.Green, 4, 10)),
        ]);

        Assert.Equal(1, For(standings, Teams.Red).Rank);
        Assert.Equal(2, For(standings, Teams.Blue).Rank);
        Assert.Equal(2, For(standings, Teams.Yellow).Rank);
        Assert.Equal(4, For(standings, Teams.Green).Rank);
    }

    [Fact]
    public void Rank_change_reports_movement_caused_by_the_last_round()
    {
        var standings = StandingsCalculator.Compute(All,
        [
            // After round 1: Red 40, Blue 30, Yellow 20, Green 10.
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 3, 20), (Teams.Green, 4, 10)),
            // Green wins big and overtakes everyone: Green 50, Red 50, Blue 50, Yellow 30.
            Round(2, (Teams.Red, 4, 10), (Teams.Blue, 3, 20), (Teams.Yellow, 2, 10), (Teams.Green, 1, 40)),
        ]);

        // Green climbed from 4th to level on 50.
        Assert.True(For(standings, Teams.Green).RankChange > 0);

        // Yellow did not move.
        Assert.Equal(4, For(standings, Teams.Yellow).Rank);
    }

    [Fact]
    public void Nobody_has_moved_after_a_single_round()
    {
        var standings = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 3, 20), (Teams.Green, 4, 10)),
        ]);

        Assert.All(standings, s => Assert.Equal(0, s.RankChange));
    }

    [Fact]
    public void Adjustments_are_added_and_reported_separately()
    {
        var standings = StandingsCalculator.Compute(All,
            [Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30))],
            new Dictionary<Guid, decimal> { [Teams.Blue] = 15m, [Teams.Green] = -5m });

        Assert.Equal(45m, For(standings, Teams.Blue).Points);
        Assert.Equal(15m, For(standings, Teams.Blue).AdjustmentPoints);

        // A penalty can take a team negative, and that is not an error.
        Assert.Equal(-5m, For(standings, Teams.Green).Points);
    }

    [Fact]
    public void Place_counts_are_tallied_per_team()
    {
        var standings = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30)),
            Round(2, (Teams.Red, 1, 40), (Teams.Blue, 2, 30)),
            Round(3, (Teams.Red, 2, 30), (Teams.Blue, 1, 40)),
        ]);

        var red = For(standings, Teams.Red).PlaceCounts;

        Assert.Equal(2, red[1]);
        Assert.Equal(1, red[2]);
        Assert.False(red.ContainsKey(3));
    }

    [Fact]
    public void A_team_absent_from_a_round_keeps_its_total_and_gains_no_place()
    {
        var standings = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 3, 20), (Teams.Green, 4, 10)),
            // Green sat this one out.
            Round(2, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 3, 20), (Teams.Green, null, 0)),
        ]);

        var green = For(standings, Teams.Green);

        Assert.Equal(10m, green.Points);

        // One counted place across two rounds: the round it sat out contributes
        // nothing rather than counting as a last place.
        Assert.Equal(1, green.PlaceCounts.Values.Sum());
        Assert.Equal(1, green.PlaceCounts[4]);
    }

    [Fact]
    public void Removing_a_round_recomputes_totals_for_every_team_including_absentees()
    {
        // This is the v1 defect, stated as a property. Voiding a round must
        // correct EVERY team's total, not only the teams that were in it.
        var withBoth = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 3, 20), (Teams.Green, 4, 10)),
            Round(2, (Teams.Red, 1, 40), (Teams.Blue, 2, 30)),
        ]);

        var withoutRound2 = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40), (Teams.Blue, 2, 30), (Teams.Yellow, 3, 20), (Teams.Green, 4, 10)),
        ]);

        Assert.Equal(80m, For(withBoth, Teams.Red).Points);
        Assert.Equal(40m, For(withoutRound2, Teams.Red).Points);

        // Yellow and Green were not in round 2 at all, and their totals are
        // still correct afterwards because nothing was ever cached.
        Assert.Equal(20m, For(withoutRound2, Teams.Yellow).Points);
        Assert.Equal(10m, For(withoutRound2, Teams.Green).Points);
    }

    [Fact]
    public void Rounds_are_ordered_by_number_not_by_the_order_supplied()
    {
        var inOrder = StandingsCalculator.Compute(All,
        [
            Round(1, (Teams.Red, 1, 40)),
            Round(2, (Teams.Red, 2, 30)),
        ]);

        var shuffled = StandingsCalculator.Compute(All,
        [
            Round(2, (Teams.Red, 2, 30)),
            Round(1, (Teams.Red, 1, 40)),
        ]);

        Assert.Equal(For(inOrder, Teams.Red).Points, For(shuffled, Teams.Red).Points);
        Assert.Equal(For(inOrder, Teams.Red).RankChange, For(shuffled, Teams.Red).RankChange);
    }
}
