namespace Awana.Data.Seeding;

/// <summary>
/// What the seeder needs that is specific to a deployment.
///
/// Nothing here is hardcoded in the repository. The church's name and its
/// leaders' email addresses come from configuration, which keeps them out of a
/// public repo and lets a second church run the same build.
/// </summary>
public class SeedOptions
{
    public const string SectionName = "Seed";

    public string ChurchName { get; set; } = "Awana Clubs";

    /// <summary>Appears in public URLs. Lowercase, hyphenated.</summary>
    public string ChurchSlug { get; set; } = "church";

    public string TimeZoneId { get; set; } = "America/Los_Angeles";

    /// <summary>
    /// Admins, comma separated, and the one list that configuration still
    /// enforces.
    /// </summary>
    /// <remarks>
    /// Everyone listed is restored to an active admin every time the server
    /// starts, whatever the People page has done to them since. That is the
    /// way back in if every admin account in the database has been demoted or
    /// deactivated, which otherwise would need somebody running SQL against
    /// production. The People page refuses to edit these accounts for the same
    /// reason: a change there would be undone by the next restart.
    /// </remarks>
    public string AdminEmails { get; set; } = string.Empty;

    /// <summary>
    /// Accounts to create if they do not exist yet. Roles for existing accounts
    /// are managed on the People page, and this list never overrides them.
    /// </summary>
    public string GamesLeaderEmails { get; set; } = string.Empty;

    /// <inheritdoc cref="GamesLeaderEmails"/>
    public string ScorekeeperEmails { get; set; } = string.Empty;

    /// <summary>The admin list, parsed.</summary>
    public IReadOnlySet<string> ConfiguredAdmins() =>
        ParseEmails(AdminEmails).ToHashSet(StringComparer.Ordinal);

    /// <summary>Splits, trims, lowercases and drops blanks and duplicates.</summary>
    public static IEnumerable<string> ParseEmails(string? raw) =>
        (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Where(e => e.Contains('@'))
            .Distinct(StringComparer.Ordinal);
}
