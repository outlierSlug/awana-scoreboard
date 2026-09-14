namespace Awana.Data.Entities;

/// <summary>
/// A game in the catalog, for example Baton Relay.
///
/// A game is a game TYPE, not a single race. One game is usually run several
/// times in an evening, so many rounds point at one game. That is what makes a
/// rotation view possible later: it counts distinct games, while the scoreboard
/// counts rounds.
///
/// Games do not carry scoring rules. Every game uses the session's points table;
/// what differs is how finishing order is decided, which is a matter for the
/// people running it rather than for the engine.
/// </summary>
public class Game
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>
    /// How the game is played, and how finishing order is decided.
    /// </summary>
    /// <remarks>
    /// The catalog is the only written record of a game that exists. Half of
    /// these were invented at this church and are carried from one year to the
    /// next by whoever ran them last, so a leader taking over needs somewhere to
    /// read what Color Cube is. Optional, and deliberately free text: this is a
    /// note to a person, not something the engine reads.
    /// </remarks>
    public string? Notes { get; set; }

    /// <summary>
    /// Where the game sits in the picker, arranged by hand in the catalog.
    /// </summary>
    /// <remarks>
    /// One list, not tiers. There used to be a flag marking the four iconic
    /// games so they sorted to the top, which turned out to be a clumsier way
    /// of saying "put these four first": a club that plays five every week, or
    /// swaps one for a season, had to argue with a category instead of dragging
    /// a row. The order is the whole answer.
    /// </remarks>
    public int SortOrder { get; set; }

    /// <summary>Retire a game with this. Rounds reference games.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Which seed row created this, for the seeder to recognize its own work.
    /// Null on anything a person added through the catalog.
    /// </summary>
    /// <remarks>
    /// The seeder used to match on name, which quietly made the name unownable:
    /// renaming Steal to Steal the Bacon left nothing matching the seed, so the
    /// next boot helpfully created Steal again and the catalog had both. A key
    /// the user cannot see and cannot edit is the thing to match on, so that
    /// every visible field belongs to the church rather than to the seed.
    /// </remarks>
    public string? SeedKey { get; set; }
}
