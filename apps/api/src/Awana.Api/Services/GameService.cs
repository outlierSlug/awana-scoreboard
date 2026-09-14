using System.Text.Json;
using Awana.Api.Contracts;
using Awana.Data;
using Awana.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Services;

/// <summary>
/// The game catalog, which is the club's list of what it plays.
/// </summary>
/// <remarks>
/// Games carry no scoring rules, and this service does not give them any. What
/// a finish is worth belongs to the session's scoring profile; a game only says
/// what is being played and, in its notes, how it goes. Keeping that line means
/// a leader can reorganize the catalog freely without any of it reaching a
/// score that has already been recorded.
///
/// Nothing here deletes history. A game that has been played can be retired,
/// which takes it out of the picker and leaves every round that references it
/// exactly as it was.
/// </remarks>
public class GameService(AwanaDbContext db)
{
    /// <summary>Matches the column, so a long note is a 400 and not a 500.</summary>
    private const int MaxNotes = 2000;

    private const int MaxName = 120;

    /// <summary>
    /// A ceiling on one reorder. Generous next to a real catalog of twenty, and
    /// low enough that the request cannot be used to make the server sort an
    /// arbitrary amount of work.
    /// </summary>
    private const int MaxReorder = 500;

    /// <summary>
    /// The catalog, retired games included, in the order the picker uses.
    /// </summary>
    public async Task<IReadOnlyList<GameDetailDto>> ListAsync(
        Guid churchId, CancellationToken ct = default) =>
        await db.Games
            .AsNoTracking()
            .Where(g => g.ChurchId == churchId)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name)
            .Select(g => new GameDetailDto(
                g.Id,
                g.Name,
                g.Notes,
                g.SortOrder,
                g.IsActive,
                // Cleared rounds are left out, the way every other count in the
                // product leaves them out. A game whose only round was cleared
                // has not been played, and should still be deletable.
                db.Rounds.Count(r => r.GameId == g.Id && r.VoidedAt == null),
                g.SeedKey != null))
            .ToListAsync(ct);

    public async Task<ServiceResult<GameDetailDto>> CreateAsync(
        Guid churchId, SaveGameRequest request, Guid? userId, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(churchId, request, existingId: null, ct);
        if (validation is not null) return ServiceResult<GameDetailDto>.Fail(validation);

        // Onto the end of the list, which is where somebody adding a game
        // expects to find it. They can drag it wherever they want from there.
        var lastSortOrder = await db.Games
            .Where(g => g.ChurchId == churchId)
            .Select(g => (int?)g.SortOrder)
            .MaxAsync(ct) ?? 0;

        var game = new Game
        {
            ChurchId = churchId,
            Name = request.Name.Trim(),
            Notes = Clean(request.Notes),
            SortOrder = lastSortOrder + 1,
        };

        db.Games.Add(game);

        AddAudit(churchId, userId, "game.created", game.Id, new { game.Name });

        await db.SaveChangesAsync(ct);

        return ServiceResult<GameDetailDto>.Success(await DetailAsync(game.Id, ct));
    }

    /// <summary>
    /// Edits a game in place, so every round already played on it keeps
    /// pointing at it and picks up the new name.
    /// </summary>
    /// <remarks>
    /// That is the intended behavior rather than a compromise: renaming Steal
    /// to Steal the Bacon is correcting what the game has always been called,
    /// not declaring that a different game was played last March. Recording a
    /// genuinely different game means adding one.
    /// </remarks>
    public async Task<ServiceResult<GameDetailDto>> UpdateAsync(
        Guid churchId, Guid id, SaveGameRequest request, Guid? userId, CancellationToken ct = default)
    {
        var game = await Find(churchId, id, ct);
        if (game is null) return ServiceResult<GameDetailDto>.Fail(ServiceError.NotFound("Game"));

        var validation = await ValidateAsync(churchId, request, existingId: id, ct);
        if (validation is not null) return ServiceResult<GameDetailDto>.Fail(validation);

        var before = new { game.Name, game.Notes };

        game.Name = request.Name.Trim();
        game.Notes = Clean(request.Notes);

        AddAudit(churchId, userId, "game.updated", game.Id, new
        {
            Before = before,
            After = new { game.Name, game.Notes },
        });

        await db.SaveChangesAsync(ct);

        return ServiceResult<GameDetailDto>.Success(await DetailAsync(game.Id, ct));
    }

    /// <summary>
    /// Takes a game out of the picker without touching what was played on it.
    /// </summary>
    /// <remarks>
    /// This is the normal way to get rid of a game, and the reason delete is
    /// nearly always refused. A season's rounds reference the game, and the
    /// board's history has to keep reading correctly in a year.
    /// </remarks>
    public async Task<ServiceResult<GameDetailDto>> SetActiveAsync(
        Guid churchId, Guid id, bool isActive, Guid? userId, CancellationToken ct = default)
    {
        var game = await Find(churchId, id, ct);
        if (game is null) return ServiceResult<GameDetailDto>.Fail(ServiceError.NotFound("Game"));

        if (game.IsActive != isActive)
        {
            game.IsActive = isActive;
            AddAudit(churchId, userId, isActive ? "game.restored" : "game.retired", game.Id, new { game.Name });
            await db.SaveChangesAsync(ct);
        }

        return ServiceResult<GameDetailDto>.Success(await DetailAsync(game.Id, ct));
    }

    /// <summary>
    /// Only ever a game that was never played. Retiring is the answer for the
    /// rest, and the error says so rather than leaving the caller to guess.
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid churchId, Guid id, Guid? userId, CancellationToken ct = default)
    {
        var game = await Find(churchId, id, ct);
        if (game is null) return ServiceResult<bool>.Fail(ServiceError.NotFound("Game"));

        // Every round, cleared ones included. A cleared round still names the
        // game in the audit trail and in the scorekeeper's history, and the
        // foreign key would refuse the delete regardless.
        var rounds = await db.Rounds.CountAsync(r => r.GameId == id, ct);

        if (rounds > 0)
        {
            return ServiceResult<bool>.Fail(ServiceError.Conflict(
                "game_in_use",
                $"{game.Name} has been played {rounds} {(rounds == 1 ? "time" : "times")}, so deleting it would break those rounds. Retire it instead: it leaves the picker and the history stays readable."));
        }

        AddAudit(churchId, userId, "game.deleted", game.Id, new { game.Name, game.SeedKey });

        db.Games.Remove(game);
        await db.SaveChangesAsync(ct);

        return ServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Rewrites the catalog order from a list of ids.
    /// </summary>
    /// <remarks>
    /// Ids not named are left where they are rather than pushed to the end,
    /// so a reorder of the core games cannot quietly shuffle the occasional
    /// ones. Ids belonging to another church, or to nothing, are ignored: this
    /// is a display preference, and failing the whole request over one stale id
    /// would mean a leader's drag is rejected because somebody else deleted a
    /// game while the page was open.
    /// </remarks>
    public async Task<ServiceResult<IReadOnlyList<GameDetailDto>>> ReorderAsync(
        Guid churchId, IReadOnlyList<Guid> ids, Guid? userId, CancellationToken ct = default)
    {
        if (ids.Count > MaxReorder)
        {
            return ServiceResult<IReadOnlyList<GameDetailDto>>.Fail(
                ServiceError.Invalid($"That is more than {MaxReorder} games."));
        }

        var games = await db.Games
            .Where(g => g.ChurchId == churchId)
            .ToDictionaryAsync(g => g.Id, ct);

        var position = 1;

        foreach (var id in ids.Distinct())
        {
            if (!games.TryGetValue(id, out var game)) continue;
            game.SortOrder = position++;
        }

        if (db.ChangeTracker.HasChanges())
        {
            AddAudit(churchId, userId, "game.reordered", Guid.Empty, new { Count = position - 1 });
            await db.SaveChangesAsync(ct);
        }

        return ServiceResult<IReadOnlyList<GameDetailDto>>.Success(await ListAsync(churchId, ct));
    }

    // --------------------------------------------------------------- helpers

    private Task<Game?> Find(Guid churchId, Guid id, CancellationToken ct) =>
        db.Games.FirstOrDefaultAsync(g => g.Id == id && g.ChurchId == churchId, ct);

    private async Task<GameDetailDto> DetailAsync(Guid id, CancellationToken ct)
    {
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == id, ct);

        var rounds = await db.Rounds.CountAsync(r => r.GameId == id && r.VoidedAt == null, ct);

        return new GameDetailDto(
            game.Id,
            game.Name,
            game.Notes,
            game.SortOrder,
            game.IsActive,
            rounds,
            game.SeedKey is not null);
    }

    private async Task<ServiceError?> ValidateAsync(
        Guid churchId, SaveGameRequest request, Guid? existingId, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0) return ServiceError.Invalid("A game needs a name.");

        if (name.Length > MaxName)
        {
            return ServiceError.Invalid($"That name is longer than {MaxName} characters.");
        }

        if (request.Notes?.Length > MaxNotes)
        {
            return ServiceError.Invalid($"Those notes are longer than {MaxNotes} characters.");
        }

        // Caught here so the answer is a sentence about a duplicate name rather
        // than the unique index surfacing as a 500.
        var duplicate = await db.Games.AnyAsync(
            g => g.ChurchId == churchId && g.Id != existingId && g.Name.ToLower() == name.ToLower(), ct);

        if (duplicate)
        {
            return ServiceError.Conflict(
                "game_name_taken",
                $"There is already a game called {name}. Check the retired ones too.");
        }

        return null;
    }

    /// <summary>Empty and whitespace both mean "no notes", not an empty note.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void AddAudit(Guid churchId, Guid? userId, string action, Guid entityId, object data) =>
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = churchId,
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(Game),
            EntityId = entityId,
            Data = JsonSerializer.SerializeToDocument(data),
        });
}
