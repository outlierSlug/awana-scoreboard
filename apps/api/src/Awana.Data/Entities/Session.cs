using Awana.Scoring;

namespace Awana.Data.Entities;

/// <summary>
/// One division's games on one night.
///
/// Sparks and T&amp;T usually play one after the other, but two sessions can be
/// live at once with each display pinned to one of them.
/// </summary>
public class Session
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    public Guid DivisionId { get; set; }
    public Division Division { get; set; } = null!;

    /// <summary>
    /// The calendar date of the night, as a date rather than a timestamp.
    ///
    /// A timestamp here would mean "which Friday was this" depends on the
    /// reader's time zone, and the church is seven or eight hours behind UTC,
    /// so a Friday evening session would routinely read as Saturday.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>Public URL segment, for example "tnt-2026-10-02". Unique.</summary>
    public required string PublicSlug { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Setup;

    /// <summary>Which profile the snapshot below was taken from, for reference only.</summary>
    public Guid? ScoringProfileId { get; set; }
    public ScoringProfile? ScoringProfile { get; set; }

    /// <summary>
    /// A FROZEN COPY of the scoring rules, taken when the session starts. Null
    /// while the session is still in Setup.
    ///
    /// This is what makes results auditable. An admin editing the church's
    /// profile in December cannot retroactively change what happened in
    /// October, because October's session is scored against its own copy.
    /// </summary>
    public ScoringConfig? ScoringConfig { get; set; }

    public int ScoringConfigSchemaVersion { get; set; } = 1;

    /// <summary>
    /// A logical clock, incremented on every mutation. NOT derived data.
    ///
    /// After a reconnect a queued real-time message can arrive later than a
    /// fresher refetch. Clients keep the highest version they have seen and
    /// discard anything older, which removes a whole class of "the board
    /// flickered back to the old score" bug.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Postgres xmin, used as an optimistic concurrency token so two
    /// simultaneous writers cannot silently overwrite each other.
    /// </summary>
    public uint RowVersion { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SessionTeam> SessionTeams { get; set; } = [];
    public ICollection<Round> Rounds { get; set; } = [];
    public ICollection<PointAdjustment> PointAdjustments { get; set; } = [];
}
