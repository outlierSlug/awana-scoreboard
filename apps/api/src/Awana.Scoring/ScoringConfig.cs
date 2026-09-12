namespace Awana.Scoring;

/// <summary>
/// How tied teams divide the slots they consumed between them.
/// </summary>
public enum TieRule
{
    /// <summary>Default. Every tied team receives the average of the consumed slots.</summary>
    AverageSharedSlots = 0,

    /// <summary>Every tied team receives the value of the best slot consumed.</summary>
    HighestSlot = 1,

    /// <summary>Every tied team receives the value of the worst slot consumed.</summary>
    LowestSlot = 2,

    /// <summary>Ties are rejected and the scorekeeper must break them.</summary>
    NoTiesAllowed = 3,
}

/// <summary>
/// What happens to a disqualified team, and to the teams behind it.
/// </summary>
public enum DqRule
{
    /// <summary>
    /// Default. The team keeps the position it finished in and earns nothing.
    /// Nobody is promoted, so disqualifying the winner does not hand the win to
    /// second place.
    /// </summary>
    ZeroButHoldSlot = 0,

    /// <summary>The team is moved behind everyone else, and the rest move up.</summary>
    DropToLast = 1,

    /// <summary>The team keeps its slot and earns <see cref="ScoringConfig.DqPenaltyPoints"/>.</summary>
    CustomPenalty = 2,

    /// <summary>The team is removed from the ordering entirely, so everyone behind it moves up.</summary>
    ZeroAndPromoteOthers = 3,
}

/// <summary>Midpoint behavior when rounding an awarded score.</summary>
public enum RoundingMode
{
    /// <summary>0.125 becomes 0.13. What people expect when checking by hand.</summary>
    HalfAwayFromZero = 0,

    /// <summary>Banker's rounding. 0.125 becomes 0.12.</summary>
    HalfToEven = 1,
}

/// <summary>How many decimal places an award is rounded to, and how ties in the last digit break.</summary>
public readonly record struct RoundingSpec(int Decimals, RoundingMode Mode)
{
    public static RoundingSpec Default => new(2, RoundingMode.HalfAwayFromZero);
}

/// <summary>
/// Everything a church can configure about scoring.
///
/// Team names, colors and counts are deliberately absent. Teams are database
/// rows with history attached, they differ between divisions, and they are
/// referenced by results. The points table's length is independent of how many
/// teams exist: a four entry table with six teams is well defined, because
/// anything past the end of the table earns <see cref="PointsBeyondTable"/>.
/// </summary>
public sealed record ScoringConfig
{
    /// <summary>Points per finishing slot, best first. The official default is 40, 30, 20, 10.</summary>
    public required IReadOnlyList<decimal> PlacePoints { get; init; }

    /// <summary>What a team earns when it finishes past the end of the table.</summary>
    public decimal PointsBeyondTable { get; init; }

    public TieRule TieRule { get; init; } = TieRule.AverageSharedSlots;

    public DqRule DqRule { get; init; } = DqRule.ZeroButHoldSlot;

    /// <summary>Used only when <see cref="DqRule"/> is <see cref="DqRule.CustomPenalty"/>.</summary>
    public decimal DqPenaltyPoints { get; init; }

    /// <summary>Whether a team may be recorded as not taking part in a round.</summary>
    public bool AllowUnplacedTeams { get; init; } = true;

    public RoundingSpec Rounding { get; init; } = RoundingSpec.Default;

    /// <summary>The official AWANA table, used to seed a new church.</summary>
    public static ScoringConfig Default { get; } = new() { PlacePoints = [40m, 30m, 20m, 10m] };
}
