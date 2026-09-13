namespace Awana.Data.Entities;

/// <summary>
/// A team taking part in a session, and optionally how many children were there.
///
/// The headcount is a COUNT and nothing else. No child is named, listed or
/// identified. A session with no headcounts recorded is completely valid, and
/// the UI treats entering them as optional.
/// </summary>
public class SessionTeam
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    /// <summary>Null means not recorded, which is different from zero present.</summary>
    public int? Headcount { get; set; }

    public DateTimeOffset? HeadcountRecordedAt { get; set; }
}
