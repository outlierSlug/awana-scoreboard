namespace Awana.Data.Entities;

/// <summary>
/// An adult leader who signs in.
///
/// These are the ONLY person-level records in the database. No child is ever
/// stored, by name or otherwise. Both fields here come from Google at sign-in.
///
/// Rows are seeded from an allowlist and never created automatically. An
/// unknown email is refused rather than granted a Viewer account.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    /// <summary>Lowercased and trimmed on write, so lookups are exact.</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>
    /// Google's stable subject claim, backfilled on first sign-in. Null until
    /// then, because rows are seeded from an email allowlist before anyone has
    /// signed in. Preferred over email for identity once present, since a
    /// person can change their email address.
    /// </summary>
    public string? GoogleSubject { get; set; }

    public UserRole Role { get; set; } = UserRole.Viewer;

    /// <summary>Deactivate rather than delete. Rounds reference who recorded them.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
