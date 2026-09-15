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

    /// <summary>
    /// Which seed row created this. Null on anything a church made itself.
    /// </summary>
    /// <remarks>
    /// A seeded set is READ ONLY, including to an admin, and that is about the
    /// name rather than about trust. A set called "Official AWANA" is a claim
    /// that it holds the official table, and one that has been edited into
    /// 50 / 25 / 15 / 10 makes the name a lie to whoever reads it next. The
    /// answer to wanting different numbers is to duplicate it, which is almost
    /// always what was meant anyway: a tournament table is the official one
    /// with a number changed, not a redefinition of the standard.
    /// </remarks>
    public string? SeedKey { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
