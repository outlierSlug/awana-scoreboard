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
    /// Restricts the game to one division. Null means every division can play
    /// it, which is the normal case. Steal was only ever run with T&amp;T.
    /// </summary>
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }

    /// <summary>
    /// The four iconic games, played most weeks. Used only to sort them to the
    /// top of the picker.
    /// </summary>
    public bool IsCore { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Retire a game with this. Rounds reference games.</summary>
    public bool IsActive { get; set; } = true;
}
