namespace Awana.Data.Seeding;

/// <summary>
/// The reference data every deployment starts with. Kept apart from the seeding
/// logic so that what gets created is readable at a glance and reviewable
/// without following code.
///
/// The game list is the real 2023-2024 catalog, so the dropdown holds actual
/// game names on the first night rather than placeholders somebody has to fix
/// standing in a gym. See docs/game-catalog.md.
/// </summary>
internal static class SeedCatalog
{
    internal sealed record DivisionSeed(string Name, string Slug, int SortOrder);

    internal sealed record TeamSeed(string Name, string ColorHex, string TextOnColorHex, int SortOrder);

    /// <param name="OnlyDivisionSlug">Null means every division can play it.</param>
    internal sealed record GameSeed(string Name, bool IsCore, int SortOrder, string? OnlyDivisionSlug = null);

    internal static readonly DivisionSeed[] Divisions =
    [
        new("Sparks", "sparks", 1),
        new("T&T", "tnt", 2),
    ];

    /// <summary>
    /// Created once per division, so Sparks Red and T&amp;T Red are separate
    /// teams with separate histories.
    ///
    /// These colors match the frontend design tokens, but the database is
    /// authoritative at render time. The tokens are only the seed values.
    /// Every pairing clears 4.5:1, which is why yellow carries dark text.
    /// </summary>
    internal static readonly TeamSeed[] Teams =
    [
        new("Red", "#dc2626", "#ffffff", 1),
        new("Blue", "#2563eb", "#ffffff", 2),
        new("Yellow", "#facc15", "#111827", 3),
        new("Green", "#15803d", "#ffffff", 4),
    ];

    internal static readonly GameSeed[] Games =
    [
        // The four iconic games, played most weeks.
        new("Baton Relay", IsCore: true, 1),
        new("Three-Legged Race", IsCore: true, 2),
        new("Scooter Relay", IsCore: true, 3),
        new("Tug-of-War", IsCore: true, 4),

        // Played occasionally, usually alongside a core game.
        new("Noodle Relay", IsCore: false, 10),
        new("Cup Stacking", IsCore: false, 11),
        new("Egg Relay", IsCore: false, 12),
        new("Balance Relay", IsCore: false, 13),
        new("Potato Sack Relay", IsCore: false, 14),
        new("Beanbag Curling", IsCore: false, 15),
        new("Prize Pool", IsCore: false, 16),
        new("Number Calling", IsCore: false, 17),
        new("Color Cube", IsCore: false, 18),

        // Only ever run with T&T.
        new("Steal", IsCore: false, 19, OnlyDivisionSlug: "tnt"),
    ];

    internal const string DefaultProfileName = "Official AWANA";
}
