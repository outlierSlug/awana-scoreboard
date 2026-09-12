namespace Awana.Scoring;

public interface IScoringEngine
{
    /// <summary>
    /// Checks whether a round can be scored. Call this before <see cref="Score"/>,
    /// which throws on invalid input.
    /// </summary>
    ValidationOutcome Validate(RoundInput input, ScoringConfig config);

    /// <summary>
    /// Scores a round. Throws <see cref="ScoringValidationException"/> if the
    /// input is invalid, so the caller is expected to have run
    /// <see cref="Validate"/> first and shown the reasons to the scorekeeper.
    /// </summary>
    ScoringOutcome Score(RoundInput input, ScoringConfig config);
}

/// <summary>
/// Thrown when <see cref="IScoringEngine.Score"/> is handed a round that does
/// not validate. This is a programming error rather than a scorekeeper error:
/// the console is expected to validate first and never submit an invalid round.
/// </summary>
public sealed class ScoringValidationException(ValidationOutcome outcome)
    : InvalidOperationException(
        "Round failed validation: " + string.Join("; ", outcome.Errors.Select(e => e.Message)))
{
    public ValidationOutcome Outcome { get; } = outcome;
}
