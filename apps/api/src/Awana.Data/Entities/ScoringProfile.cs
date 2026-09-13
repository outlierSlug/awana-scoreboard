using Awana.Scoring;

namespace Awana.Data.Entities;

/// <summary>
/// A named set of scoring rules a church can edit.
///
/// The rules themselves live in <see cref="Config"/> as a single jsonb document
/// rather than a set of columns. It is read whole and never queried by field, so
/// normalizing it would cost three or four tables and a migration for every new
/// knob. Validation is not lost: the pure library validates on write.
/// </summary>
public class ScoringProfile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>Stored as jsonb.</summary>
    public required ScoringConfig Config { get; set; }

    /// <summary>
    /// Lets a future release recognize and migrate an older document shape
    /// rather than guessing at it.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Used for new sessions when none is chosen explicitly.</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
