namespace Awana.Scoring.Tests;

/// <summary>
/// Fixed team ids so failures name a colour rather than a random guid.
/// </summary>
internal static class Teams
{
    public static readonly Guid Red = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Blue = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Yellow = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Green = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Purple = new("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Orange = new("66666666-6666-6666-6666-666666666666");

    public static readonly Guid[] Four = [Red, Blue, Yellow, Green];
}

internal static class TestExtensions
{
    public static decimal PointsFor(this ScoringOutcome outcome, Guid teamId) =>
        outcome.Awards.Single(a => a.TeamId == teamId).Points;

    public static TeamAward AwardFor(this ScoringOutcome outcome, Guid teamId) =>
        outcome.Awards.Single(a => a.TeamId == teamId);
}
