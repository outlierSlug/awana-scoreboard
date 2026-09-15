namespace Awana.Scoring.Tests;

/// <summary>
/// These cases are the worked examples in docs/scoring-rules.md. If one changes
/// here it must change there too, because that document is what gets printed and
/// used to settle a disputed round in the gym.
/// </summary>
public class ScoringEngineTests
{
    private static readonly ScoringEngine Engine = new();

    private static ScoringOutcome ScoreFour(
        int? red, int? blue, int? yellow, int? green,
        ScoringConfig? config = null,
        decimal multiplier = 1m,
        params Guid[] disqualified)
    {
        var places = new[] { red, blue, yellow, green };

        var entries = Teams.Four
            .Select((id, i) => new TeamEntry(id, places[i], disqualified.Contains(id)))
            .ToList();

        return Engine.Score(new RoundInput(entries, multiplier), config ?? ScoringConfig.Default);
    }

    // ------------------------------------------------------------- placements

    [Theory]
    // Clean finish, no ties.
    [InlineData(1, 2, 3, 4, 40, 30, 20, 10)]
    // Two teams tied for first consume slots 1 and 2, worth 70 between them.
    // The third team takes slot 3 and earns 20, not 30.
    [InlineData(1, 1, 3, 4, 35, 35, 20, 10)]
    [InlineData(1, 2, 2, 4, 40, 25, 25, 10)]
    [InlineData(1, 2, 3, 3, 40, 30, 15, 15)]
    [InlineData(1, 1, 3, 3, 35, 35, 15, 15)]
    [InlineData(1, 1, 1, 4, 30, 30, 30, 10)]
    [InlineData(1, 2, 2, 2, 40, 20, 20, 20)]
    [InlineData(1, 1, 1, 1, 25, 25, 25, 25)]
    public void Awards_match_the_published_truth_table(
        int red, int blue, int yellow, int green,
        double expectedRed, double expectedBlue, double expectedYellow, double expectedGreen)
    {
        var outcome = ScoreFour(red, blue, yellow, green);

        Assert.Equal((decimal)expectedRed, outcome.PointsFor(Teams.Red));
        Assert.Equal((decimal)expectedBlue, outcome.PointsFor(Teams.Blue));
        Assert.Equal((decimal)expectedYellow, outcome.PointsFor(Teams.Yellow));
        Assert.Equal((decimal)expectedGreen, outcome.PointsFor(Teams.Green));
    }

    [Theory]
    // Gaps in the recorded places must not change anything.
    [InlineData(1, 2, 4, 7)]
    [InlineData(2, 4, 6, 8)]
    [InlineData(10, 20, 30, 40)]
    public void Non_contiguous_places_are_normalized(int red, int blue, int yellow, int green)
    {
        var outcome = ScoreFour(red, blue, yellow, green);

        Assert.Equal(40m, outcome.PointsFor(Teams.Red));
        Assert.Equal(30m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(10m, outcome.PointsFor(Teams.Green));
    }

    [Theory]
    // A tie consumes more than one slot, so the team after it does NOT simply
    // take the next ordinal. Two teams tied for 1st use slots 1 and 2, which
    // puts the next team 3rd. Getting this wrong labels a round incorrectly on
    // the board while the points stay right, so it is easy to miss.
    [InlineData(1, 2, 3, 4, /* expect */ 1, 2, 3, 4)]
    [InlineData(1, 1, 3, 4, /* expect */ 1, 1, 3, 4)]
    [InlineData(1, 2, 2, 4, /* expect */ 1, 2, 2, 4)]
    [InlineData(1, 2, 3, 3, /* expect */ 1, 2, 3, 3)]
    [InlineData(1, 1, 3, 3, /* expect */ 1, 1, 3, 3)]
    [InlineData(1, 1, 1, 4, /* expect */ 1, 1, 1, 4)]
    [InlineData(1, 2, 2, 2, /* expect */ 1, 2, 2, 2)]
    [InlineData(1, 1, 1, 1, /* expect */ 1, 1, 1, 1)]
    // Gaps in the input still normalize to the slots actually consumed.
    [InlineData(1, 2, 4, 7, /* expect */ 1, 2, 3, 4)]
    [InlineData(2, 2, 5, 9, /* expect */ 1, 1, 3, 4)]
    public void Finishing_place_is_the_slot_consumed_not_the_group_ordinal(
        int red, int blue, int yellow, int green,
        int expectedRed, int expectedBlue, int expectedYellow, int expectedGreen)
    {
        var outcome = ScoreFour(red, blue, yellow, green);

        Assert.Equal(expectedRed, outcome.AwardFor(Teams.Red).Place);
        Assert.Equal(expectedBlue, outcome.AwardFor(Teams.Blue).Place);
        Assert.Equal(expectedYellow, outcome.AwardFor(Teams.Yellow).Place);
        Assert.Equal(expectedGreen, outcome.AwardFor(Teams.Green).Place);
    }

    [Fact]
    public void The_explanation_names_the_same_place_it_scored()
    {
        // Red and Blue tie for 1st, so Yellow is 3rd on 20 and Green 4th on 10.
        var outcome = ScoreFour(1, 1, 3, 4);

        Assert.Contains("3rd place", outcome.AwardFor(Teams.Yellow).Explanation);
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));

        Assert.Contains("4th place", outcome.AwardFor(Teams.Green).Explanation);
        Assert.Equal(10m, outcome.PointsFor(Teams.Green));
    }

    [Fact]
    public void A_tie_below_another_tie_is_named_by_its_own_slot()
    {
        // Red and Blue tie for 1st; Yellow and Green tie for 3rd, not 2nd.
        var outcome = ScoreFour(1, 1, 3, 3);

        Assert.Contains("Tied for 3rd", outcome.AwardFor(Teams.Yellow).Explanation);
        Assert.Equal(15m, outcome.PointsFor(Teams.Yellow));
    }

    // ---------------------------------------------------------- disqualified

    [Fact]
    public void Disqualified_winner_earns_nothing_and_promotes_nobody()
    {
        var outcome = ScoreFour(1, 2, 3, 4, disqualified: Teams.Red);

        Assert.Equal(0m, outcome.PointsFor(Teams.Red));
        // Blue stays on 30. It does not inherit first place.
        Assert.Equal(30m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(10m, outcome.PointsFor(Teams.Green));
    }

    [Fact]
    public void Disqualified_team_inside_a_tie_forfeits_its_share_rather_than_donating_it()
    {
        // Red and Blue tie for first. The group still consumes slots 1 and 2 and
        // the average is still over both members, so Blue earns 35 and not 70.
        // Otherwise a disqualification would reward whoever tied with them.
        var outcome = ScoreFour(1, 1, 3, 4, disqualified: Teams.Red);

        Assert.Equal(0m, outcome.PointsFor(Teams.Red));
        Assert.Equal(35m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(10m, outcome.PointsFor(Teams.Green));
    }

    [Fact]
    public void All_teams_disqualified_scores_zero_across_the_board()
    {
        var outcome = ScoreFour(1, 2, 3, 4, disqualified: Teams.Four);

        Assert.All(outcome.Awards, a => Assert.Equal(0m, a.Points));
        Assert.All(outcome.Awards, a => Assert.True(a.IsDisqualified));
    }

    [Fact]
    public void DropToLast_moves_the_disqualified_team_behind_everyone()
    {
        var config = ScoringConfig.Default with { DqRule = DqRule.DropToLast };
        var outcome = ScoreFour(1, 2, 3, 4, config, disqualified: Teams.Red);

        // Red vacates first, so everyone else moves up a slot.
        Assert.Equal(0m, outcome.PointsFor(Teams.Red));
        Assert.Equal(40m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(30m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(20m, outcome.PointsFor(Teams.Green));
    }

    [Fact]
    public void ZeroAndPromoteOthers_removes_the_team_from_the_ordering()
    {
        var config = ScoringConfig.Default with { DqRule = DqRule.ZeroAndPromoteOthers };
        var outcome = ScoreFour(1, 2, 3, 4, config, disqualified: Teams.Red);

        Assert.Equal(0m, outcome.PointsFor(Teams.Red));
        Assert.Equal(40m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(30m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(20m, outcome.PointsFor(Teams.Green));
    }

    [Fact]
    public void CustomPenalty_awards_the_configured_value_and_holds_the_slot()
    {
        var config = ScoringConfig.Default with
        {
            DqRule = DqRule.CustomPenalty,
            DqPenaltyPoints = -5m,
        };

        var outcome = ScoreFour(1, 2, 3, 4, config, disqualified: Teams.Red);

        Assert.Equal(-5m, outcome.PointsFor(Teams.Red));
        Assert.Equal(30m, outcome.PointsFor(Teams.Blue));
    }

    // ---------------------------------------------------------------- absent

    [Fact]
    public void Absent_teams_consume_no_slot_and_shift_nobody()
    {
        var outcome = ScoreFour(1, 2, 3, null);

        Assert.Equal(40m, outcome.PointsFor(Teams.Red));
        Assert.Equal(30m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(0m, outcome.PointsFor(Teams.Green));
        Assert.Null(outcome.AwardFor(Teams.Green).Place);
    }

    // ------------------------------------------------------------- tie rules

    [Fact]
    public void HighestSlot_gives_every_tied_team_the_best_slot()
    {
        var config = ScoringConfig.Default with { TieRule = TieRule.HighestSlot };
        var outcome = ScoreFour(1, 1, 3, 4, config);

        Assert.Equal(40m, outcome.PointsFor(Teams.Red));
        Assert.Equal(40m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));
    }

    [Fact]
    public void LowestSlot_gives_every_tied_team_the_worst_slot()
    {
        var config = ScoringConfig.Default with { TieRule = TieRule.LowestSlot };
        var outcome = ScoreFour(1, 1, 3, 4, config);

        Assert.Equal(30m, outcome.PointsFor(Teams.Red));
        Assert.Equal(30m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));
    }

    [Fact]
    public void NoTiesAllowed_rejects_a_tied_round()
    {
        var config = ScoringConfig.Default with { TieRule = TieRule.NoTiesAllowed };

        var entries = Teams.Four
            .Select((id, i) => new TeamEntry(id, i == 1 ? 1 : i + 1))
            .ToList();

        var validation = Engine.Validate(new RoundInput(entries), config);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.TiesNotAllowed);
        Assert.Throws<ScoringValidationException>(() => Engine.Score(new RoundInput(entries), config));
    }

    [Fact]
    public void NoTiesAllowed_accepts_a_clean_round()
    {
        var config = ScoringConfig.Default with { TieRule = TieRule.NoTiesAllowed };
        var outcome = ScoreFour(1, 2, 3, 4, config);

        Assert.Equal(40m, outcome.PointsFor(Teams.Red));
    }

    // --------------------------------------------------- table size and teams

    [Fact]
    public void Teams_past_the_end_of_the_table_earn_the_beyond_table_value()
    {
        var entries = new List<TeamEntry>
        {
            new(Teams.Red, 1), new(Teams.Blue, 2), new(Teams.Yellow, 3),
            new(Teams.Green, 4), new(Teams.Purple, 5), new(Teams.Orange, 6),
        };

        var outcome = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        Assert.Equal(10m, outcome.PointsFor(Teams.Green));
        Assert.Equal(0m, outcome.PointsFor(Teams.Purple));
        Assert.Equal(0m, outcome.PointsFor(Teams.Orange));
    }

    [Fact]
    public void Beyond_table_value_is_configurable()
    {
        var config = ScoringConfig.Default with { PointsBeyondTable = 5m };

        var entries = new List<TeamEntry>
        {
            new(Teams.Red, 1), new(Teams.Blue, 2), new(Teams.Yellow, 3),
            new(Teams.Green, 4), new(Teams.Purple, 5),
        };

        var outcome = Engine.Score(new RoundInput(entries), config);

        Assert.Equal(5m, outcome.PointsFor(Teams.Purple));
    }

    [Fact]
    public void Fewer_teams_than_the_table_simply_leaves_slots_unused()
    {
        var entries = new List<TeamEntry>
        {
            new(Teams.Red, 1), new(Teams.Blue, 2), new(Teams.Yellow, 3),
        };

        var outcome = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        Assert.Equal(40m, outcome.PointsFor(Teams.Red));
        Assert.Equal(30m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(20m, outcome.PointsFor(Teams.Yellow));
    }

    // -------------------------------------------------------------- rounding

    [Fact]
    public void Uneven_splits_round_to_the_configured_precision()
    {
        // Slots 2, 3 and 4 are worth 25 + 15 + 10 = 50, split three ways.
        var config = ScoringConfig.Default with
        {
            PlacePoints = [50m, 25m, 15m, 10m],
            Rounding = new RoundingSpec(2, RoundingMode.HalfAwayFromZero),
        };

        var outcome = ScoreFour(1, 2, 2, 2, config);

        Assert.Equal(50m, outcome.PointsFor(Teams.Red));
        Assert.Equal(16.67m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(16.67m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(16.67m, outcome.PointsFor(Teams.Green));
    }

    [Fact]
    public void Official_table_never_needs_a_decimal()
    {
        // The reason whole numbers are the default. Every split on the official
        // table lands on an integer, so nothing is lost by hiding decimals.
        int?[][] patterns =
        [
            [1, 2, 3, 4], [1, 1, 3, 4], [1, 2, 2, 4], [1, 2, 3, 3],
            [1, 1, 3, 3], [1, 1, 1, 4], [1, 2, 2, 2], [1, 1, 1, 1],
        ];

        foreach (var places in patterns)
        {
            var outcome = ScoreFour(places[0], places[1], places[2], places[3]);

            Assert.All(outcome.Awards, a =>
                Assert.True(
                    a.Points == decimal.Truncate(a.Points),
                    $"Pattern [{string.Join(",", places)}] produced {a.Points}, which is not whole."));
        }
    }

    [Fact]
    public void Rounding_mode_is_configurable()
    {
        var awayFromZero = ScoringConfig.Default with
        {
            PlacePoints = [0.125m],
            Rounding = new RoundingSpec(2, RoundingMode.HalfAwayFromZero),
        };

        var toEven = awayFromZero with { Rounding = new RoundingSpec(2, RoundingMode.HalfToEven) };

        var entries = new List<TeamEntry> { new(Teams.Red, 1) };

        Assert.Equal(0.13m, Engine.Score(new RoundInput(entries), awayFromZero).PointsFor(Teams.Red));
        Assert.Equal(0.12m, Engine.Score(new RoundInput(entries), toEven).PointsFor(Teams.Red));
    }

    // -------------------------------------------------- multiplier and bonus

    [Fact]
    public void Multiplier_applies_to_placement_points()
    {
        var outcome = ScoreFour(1, 2, 3, 4, multiplier: 2m);

        Assert.Equal(80m, outcome.PointsFor(Teams.Red));
        Assert.Equal(60m, outcome.PointsFor(Teams.Blue));
        Assert.Equal(40m, outcome.PointsFor(Teams.Yellow));
        Assert.Equal(20m, outcome.PointsFor(Teams.Green));
    }

    [Fact]
    public void Bonus_is_added_after_the_multiplier_and_is_never_doubled()
    {
        var entries = new List<TeamEntry>
        {
            new(Teams.Red, 1, Bonus: 5m),
            new(Teams.Blue, 2),
        };

        var outcome = Engine.Score(new RoundInput(entries, Multiplier: 2m), ScoringConfig.Default);

        // 40 doubled is 80, plus a flat 5 bonus. Not (40 + 5) * 2.
        Assert.Equal(85m, outcome.PointsFor(Teams.Red));
        Assert.Equal(60m, outcome.PointsFor(Teams.Blue));
    }

    // ------------------------------------------------------------ validation

    [Fact]
    public void Empty_round_is_rejected()
    {
        var validation = Engine.Validate(new RoundInput([]), ScoringConfig.Default);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.EmptyRound);
    }

    [Fact]
    public void Duplicate_team_is_rejected()
    {
        var entries = new List<TeamEntry> { new(Teams.Red, 1), new(Teams.Red, 2) };

        var validation = Engine.Validate(new RoundInput(entries), ScoringConfig.Default);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.DuplicateTeam);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Place_below_one_is_rejected(int place)
    {
        var entries = new List<TeamEntry> { new(Teams.Red, place) };

        var validation = Engine.Validate(new RoundInput(entries), ScoringConfig.Default);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.InvalidPlace);
    }

    [Fact]
    public void Negative_multiplier_is_rejected()
    {
        var entries = new List<TeamEntry> { new(Teams.Red, 1) };

        var validation = Engine.Validate(new RoundInput(entries, Multiplier: -1m), ScoringConfig.Default);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.NegativeMultiplier);
    }

    [Fact]
    public void A_multiplier_far_beyond_any_real_one_is_rejected()
    {
        // Points are stored in a fixed-precision column. Without a ceiling a
        // mistyped multiplier overflows it, and a typo comes back as a server
        // error rather than as the mistake it is.
        var entries = new List<TeamEntry> { new(Teams.Red, 1) };

        var validation = Engine.Validate(
            new RoundInput(entries, Multiplier: ScoringEngine.MaxMultiplier + 1m), ScoringConfig.Default);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.MultiplierTooLarge);
    }

    [Fact]
    public void A_negative_bonus_is_rejected()
    {
        // It was accepted, and silently took points away. A bonus is something
        // a team earned; subtracting with it turns the bonus box into a penalty
        // box nobody designed and nobody could later explain.
        var entries = new List<TeamEntry> { new(Teams.Red, 1, Bonus: -500m) };

        var validation = Engine.Validate(new RoundInput(entries, Multiplier: 1m), ScoringConfig.Default);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.NegativeBonus);
    }

    [Fact]
    public void A_bonus_far_beyond_any_real_one_is_rejected()
    {
        var entries = new List<TeamEntry> { new(Teams.Red, 1, Bonus: ScoringEngine.MaxBonus + 1m) };

        var validation = Engine.Validate(new RoundInput(entries, Multiplier: 1m), ScoringConfig.Default);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.BonusTooLarge);
    }

    [Fact]
    public void An_ordinary_bonus_is_still_fine()
    {
        // The guard rejects the absurd, not the real. Twenty points is what the
        // bonus bucket has historically been worth.
        var entries = new List<TeamEntry> { new(Teams.Red, 1, Bonus: 20m) };

        Assert.True(Engine.Validate(new RoundInput(entries, Multiplier: 2m), ScoringConfig.Default).IsValid);
    }

    [Fact]
    public void Unplaced_team_is_rejected_when_the_profile_forbids_it()
    {
        var config = ScoringConfig.Default with { AllowUnplacedTeams = false };
        var entries = new List<TeamEntry> { new(Teams.Red, 1), new(Teams.Blue, null) };

        var validation = Engine.Validate(new RoundInput(entries), config);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.UnplacedNotAllowed);
    }

    [Fact]
    public void Empty_points_table_is_rejected()
    {
        var config = ScoringConfig.Default with { PlacePoints = [] };
        var entries = new List<TeamEntry> { new(Teams.Red, 1) };

        var validation = Engine.Validate(new RoundInput(entries), config);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == ScoringErrorCodes.EmptyPointsTable);
    }

    // ----------------------------------------------------------- explanation

    [Fact]
    public void Explanation_names_the_team_that_was_tied_with()
    {
        var entries = new List<TeamEntry>
        {
            new(Teams.Red, 1, DisplayName: "Red"),
            new(Teams.Blue, 1, DisplayName: "Blue"),
            new(Teams.Yellow, 3, DisplayName: "Yellow"),
        };

        var outcome = Engine.Score(new RoundInput(entries), ScoringConfig.Default);

        var red = outcome.AwardFor(Teams.Red).Explanation;

        Assert.Contains("Tied for 1st with Blue", red);
        Assert.Contains("35", red);
    }

    [Fact]
    public void Explanation_still_works_without_display_names()
    {
        var outcome = ScoreFour(1, 1, 3, 4);

        Assert.Contains("1 other team", outcome.AwardFor(Teams.Red).Explanation);
    }

    [Fact]
    public void Explanation_states_that_a_disqualification_promoted_nobody()
    {
        var outcome = ScoreFour(1, 2, 3, 4, disqualified: Teams.Red);

        var red = outcome.AwardFor(Teams.Red).Explanation;

        Assert.Contains("disqualified", red);
        Assert.Contains("no team was promoted", red);
    }
    // -------------------------------------------------- fractional rounding

    /// <summary>
    /// A table whose ties do not divide evenly, which the official one always
    /// does. Reported from the app on 2026-09-14: 100 / 75 / 50 / 25 with one
    /// decimal place showed a four-way tie as 63 rather than 62.5. The engine
    /// was right and every screen was rounding the answer away, but nothing
    /// pinned the engine's half of it down.
    /// </summary>
    [Fact]
    public void A_table_that_divides_unevenly_keeps_the_decimals_it_was_configured_for()
    {
        var config = new ScoringConfig
        {
            PlacePoints = [100m, 75m, 50m, 25m],
            Rounding = new RoundingSpec(1, RoundingMode.HalfAwayFromZero),
        };

        // All four level share the whole table: 250 over four is 62.5 each.
        var outcome = ScoreFour(1, 1, 1, 1, config);

        Assert.All(outcome.Awards, a => Assert.Equal(62.5m, a.Points));
        Assert.Equal(250m, outcome.TotalAwarded);
    }

    [Fact]
    public void Whole_number_rounding_still_collapses_the_same_tie()
    {
        // The default spec, and the reason this went unnoticed: on the official
        // table every possible split is already whole.
        var config = new ScoringConfig { PlacePoints = [100m, 75m, 50m, 25m] };

        var outcome = ScoreFour(1, 1, 1, 1, config);

        Assert.All(outcome.Awards, a => Assert.Equal(63m, a.Points));
    }

    [Fact]
    public void Two_decimal_places_survive_a_three_way_split()
    {
        var config = new ScoringConfig
        {
            PlacePoints = [100m, 75m, 50m, 25m],
            Rounding = new RoundingSpec(2, RoundingMode.HalfAwayFromZero),
        };

        // First three tie: 100 + 75 + 50 over three is 75 exactly, and the
        // fourth team keeps 25.
        var outcome = ScoreFour(1, 1, 1, 2, config);

        Assert.Equal(3, outcome.Awards.Count(a => a.Points == 75m));
        Assert.Equal(25m, outcome.Awards.Single(a => a.Place == 4).Points);
    }
}
