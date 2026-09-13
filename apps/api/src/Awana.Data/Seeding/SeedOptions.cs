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
    /// Role allowlists, comma separated. An email absent from all three cannot
    /// sign in: the API refuses it rather than creating a Viewer account.
    /// </summary>
    public string AdminEmails { get; set; } = string.Empty;

    public string GamesLeaderEmails { get; set; } = string.Empty;

    public string ScorekeeperEmails { get; set; } = string.Empty;
}
