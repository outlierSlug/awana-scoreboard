namespace Awana.Data.Entities;

/// <summary>
/// A color team within one division.
///
/// Sparks Red and T&amp;T Red are separate rows with separate histories, not one
/// shared color. That is why teams belong to a division rather than to the
/// church, and why the scoring engine takes team ids rather than a color list.
/// </summary>
public class Team
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    public Guid DivisionId { get; set; }
    public Division Division { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>
    /// The authoritative color at render time. The frontend's design tokens are
    /// only seed values for this column, never the other way round.
    /// </summary>
    public required string ColorHex { get; set; }

    /// <summary>
    /// What to write ON the team color. Held explicitly because no single
    /// choice works for every team: white is unreadable on yellow, and yellow
    /// is one of the four.
    /// </summary>
    public required string TextOnColorHex { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Retire a team with this rather than deleting it. Results reference teams,
    /// and deleting one would erase history.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
