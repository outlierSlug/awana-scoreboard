namespace Awana.Scoring;

/// <summary>
/// What one team earned, and why.
/// </summary>
/// <param name="Explanation">
/// A first-class output, not a debug string. It is shown in the scorekeeper's
/// confirm panel before the round is saved, and stored alongside the result so
/// a round can be explained weeks later without recomputing it.
/// </param>
public sealed record TeamAward(
    Guid TeamId,
    int? Place,
    decimal Points,
    bool IsDisqualified,
    string Explanation);

/// <summary>The result of scoring a round.</summary>
public sealed record ScoringOutcome(IReadOnlyList<TeamAward> Awards)
{
    public decimal TotalAwarded => Awards.Sum(a => a.Points);
}

/// <summary>One reason a round or a configuration was rejected.</summary>
/// <param name="Code">Stable, machine readable, safe to branch on.</param>
/// <param name="Message">Written for the scorekeeper, not the developer.</param>
public sealed record ValidationError(string Code, string Message);

/// <summary>
/// Whether a round can be scored. Returned rather than thrown, because an
/// invalid round is an ordinary thing for a scorekeeper to produce and the
/// console needs to say precisely what is wrong.
/// </summary>
public sealed record ValidationOutcome(IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static ValidationOutcome Valid { get; } = new([]);

    public static ValidationOutcome Invalid(string code, string message) =>
        new([new ValidationError(code, message)]);
}

/// <summary>Error codes the API and the console branch on.</summary>
public static class ScoringErrorCodes
{
    public const string EmptyRound = "scoring.empty_round";
    public const string DuplicateTeam = "scoring.duplicate_team";
    public const string InvalidPlace = "scoring.invalid_place";
    public const string TiesNotAllowed = "scoring.ties_not_allowed";
    public const string UnplacedNotAllowed = "scoring.unplaced_not_allowed";
    public const string NegativeMultiplier = "scoring.negative_multiplier";
    public const string MultiplierTooLarge = "scoring.multiplier_too_large";
    public const string NegativeBonus = "scoring.negative_bonus";
    public const string BonusTooLarge = "scoring.bonus_too_large";
    public const string EmptyPointsTable = "scoring.empty_points_table";
    public const string InvalidRounding = "scoring.invalid_rounding";
}
