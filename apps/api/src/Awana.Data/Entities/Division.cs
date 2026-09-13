namespace Awana.Data.Entities;

/// <summary>
/// An age group that competes as its own set of teams, such as Sparks or T&amp;T.
/// Each division has its own teams and its own sessions.
/// </summary>
public class Division
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>Used to build a session's public slug, for example "tnt".</summary>
    public required string Slug { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Team> Teams { get; set; } = [];
}
