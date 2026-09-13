namespace Awana.Data.Entities;

/// <summary>
/// What one team did in one round, and what it earned.
///
/// These rows are the ONLY record of points. There is no running-total table:
/// standings are summed from here on every read. Four teams by eight rounds is
/// thirty-two rows, so the sum is cheaper than serializing its own result, and
/// with a single representation the totals cannot drift.
///
/// The v1 draft kept a hand-maintained cache alongside these and it went wrong
/// exactly as that pattern does, most visibly when deleting a round left teams
/// absent from it holding stale totals.
/// </summary>
public class RoundResult
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RoundId { get; set; }
    public Round Round { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    /// <summary>
    /// Finishing position, 1 being first. TIED TEAMS SHARE A VALUE.
    /// Null means the team did not take part, which is not the same as last.
    /// </summary>
    public int? Place { get; set; }

    public bool IsDisqualified { get; set; }

    /// <summary>Placement points after any multiplier, excluding the bonus.</summary>
    public decimal PointsAwarded { get; set; }

    /// <summary>Added on top, never multiplied. The bonus bucket is the usual case.</summary>
    public decimal BonusPoints { get; set; }

    public string? BonusReason { get; set; }

    /// <summary>
    /// The arithmetic in words, for example "Tied for 1st with Blue. Places 1
    /// and 2 share 40 + 30 = 70, split 2 ways, 35 each."
    ///
    /// Stored rather than recomputed so a round can be explained weeks later
    /// even if the profile has changed since.
    /// </summary>
    public required string Explanation { get; set; }
}
