namespace Awana.Data.Entities;

/// <summary>
/// The tenant. v1 ships with exactly one row and no signup, but every
/// tenant-scoped table carries a non-nullable ChurchId from the start.
///
/// The v1 draft made its equivalent column nullable and "optional", which is
/// how tenancy ends up bolted on afterwards and leaking between churches.
/// </summary>
public class Church
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>Used in public URLs. Lowercase, hyphenated.</summary>
    public required string Slug { get; set; }

    /// <summary>
    /// IANA identifier, for example "America/Los_Angeles". Used to decide which
    /// calendar day a session belongs to. Timestamps themselves are always UTC.
    /// </summary>
    public required string TimeZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Division> Divisions { get; set; } = [];
    public ICollection<Game> Games { get; set; } = [];
    public ICollection<ScoringProfile> ScoringProfiles { get; set; } = [];
    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<Session> Sessions { get; set; } = [];
}
