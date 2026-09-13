namespace Awana.Data.Entities;

/// <summary>
/// Points given or taken outside the games, for example a sportsmanship award
/// or a penalty. Always carries a written reason.
///
/// Kept separate from round results so that "what happened in the games" and
/// "what a leader decided" stay distinguishable when explaining a total.
/// </summary>
public class PointAdjustment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    /// <summary>May be negative.</summary>
    public decimal Points { get; set; }

    /// <summary>Required. An unexplained adjustment is indistinguishable from a bug.</summary>
    public required string Reason { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Voided rather than deleted, like rounds.</summary>
    public DateTimeOffset? VoidedAt { get; set; }

    public Guid? VoidedByUserId { get; set; }
    public AppUser? VoidedByUser { get; set; }
}
