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

    /// <param name="Key">
    /// Stable, invisible, and never shown or edited. It is what the seeder
    /// matches on, so that the name is the church's to change.
    /// </param>
    /// <param name="Notes">
    /// Left null where nobody has written down how the game is actually run
    /// here. An empty note the catalog invites somebody to fill in is honest;
    /// a guessed one is worse than nothing, because it reads as the record.
    /// </param>
    internal sealed record GameSeed(
        string Key,
        string Name,
        int SortOrder,
        string? Notes = null);

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

    /// <summary>
    /// A starting point, not a fixed list. Everything here is editable in the
    /// catalog, and a church that plays none of these can retire the lot and
    /// add its own. What the seed buys is a first night with real names in the
    /// picker instead of placeholders somebody has to fix standing in a gym.
    /// </summary>
    internal static readonly GameSeed[] Games =
    [
        // The four iconic games first, because they are played most weeks.
        // Only a starting order: it is dragged into shape in the catalog.
        new("baton-relay", "Baton Relay", 1,
            "Teams line up and run a leg each, passing a baton. First team to bring the baton home wins the round."),
        new("three-legged-race", "Three-Legged Race", 2,
            "Two clubbers per team run a leg with adjacent ankles tied together. Decided by finishing order."),
        new("scooter-relay", "Scooter Relay", 3,
            "Each runner crosses the circle and back on a gym scooter before the next goes. Decided by finishing order."),
        new("tug-of-war", "Tug-of-War", 4,
            "Run head to head rather than all four at once. Often the closing game, which is where a round multiplier tends to get used."),

        // Played occasionally, usually alongside a core game.
        new("noodle-relay", "Noodle Relay", 5,
            "A relay run carrying or balancing a pool noodle. Decided by finishing order."),
        new("cup-stacking", "Cup Stacking", 6,
            "Each runner stacks or unstacks a set of cups at the far end before returning. First team through its whole line wins."),
        new("egg-relay", "Egg Relay", 7,
            "A relay carrying an egg, usually plastic, on a spoon. Drop it and the runner restarts that leg."),
        new("balance-relay", "Balance Relay", 8,
            "A relay carrying something awkward, such as a beanbag on the head. Decided by finishing order."),
        new("potato-sack-relay", "Potato Sack Relay", 9,
            "A relay hopping in a sack. Decided by finishing order."),

        // Invented or adapted here. The rules are left empty on purpose: only
        // the leaders who run these know how they actually go, and the catalog
        // is where they should write it down.
        new("beanbag-curling", "Beanbag Curling", 10,
            "Slid rather than raced: finishing order comes from a curling style distance score rather than who crossed first."),
        new("prize-pool", "Prize Pool", 11,
            "A hula hoop of assorted items sits in the center circle. Everyone on each team goes once "
            + "around the circle, then into the middle to grab one item. When the hoop is empty or time "
            + "is up, price the items up and total each team's haul: the totals decide the finishing "
            + "order. A longer game than most, so a round of it is often worth a multiplier."),
        new("number-calling", "Number Calling", 12),
        new("color-cube", "Color Cube", 13),

        new("steal", "Steal", 14),
    ];

    internal const string DefaultProfileName = "Official AWANA";

    /// <summary>Stable and invisible. A set carrying it is read only.</summary>
    internal const string DefaultProfileKey = "official-awana";
}
