using Awana.Api.Contracts;
using Awana.Api.Services;
using Awana.Data;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Endpoints;

public static class ApiEndpoints
{
    public static void MapAwanaApi(this WebApplication app)
    {
        MapPublic(app);
        MapCatalog(app);
        MapSessions(app);
        MapRounds(app);
    }

    // --------------------------------------------------------------- public

    /// <summary>
    /// No sign-in, ever. The board is a URL somebody types into a TV.
    ///
    /// This group is kept separate precisely so that when authorization is
    /// added it is attached to the OTHER groups and this one is visibly, and
    /// deliberately, left open.
    /// </summary>
    private static void MapPublic(WebApplication app)
    {
        var group = app.MapGroup("/api/public").WithTags("Public");

        group.MapGet("/live", async (string church, SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.ListLiveAsync(church, ct)));

        group.MapGet("/finished", async (string church, SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.ListFinishedAsync(church, ct: ct)));

        group.MapGet("/sessions/{slug}/scoreboard",
            async (string slug, ScoreboardService scoreboard, CancellationToken ct) =>
            {
                var board = await scoreboard.BuildBySlugAsync(slug, ct);
                return board is null
                    ? Problems.From(ServiceError.NotFound("Session"))
                    : Results.Ok(board);
            });
    }

    // -------------------------------------------------------------- catalog

    private static void MapCatalog(WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Catalog");

        group.MapGet("/divisions", async (AwanaDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Divisions
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.SortOrder)
                .Select(d => new { d.Id, d.Name, d.Slug })
                .ToListAsync(ct)));

        group.MapGet("/games", async (AwanaDbContext db, Guid? divisionId, CancellationToken ct) =>
            Results.Ok(await db.Games
                .AsNoTracking()
                .Where(g => g.IsActive)
                // A game restricted to one division is hidden from the others.
                .Where(g => g.DivisionId == null || divisionId == null || g.DivisionId == divisionId)
                .OrderByDescending(g => g.IsCore)
                .ThenBy(g => g.SortOrder)
                .Select(g => new GameDto(g.Id, g.Name, g.IsCore, g.DivisionId))
                .ToListAsync(ct)));
    }

    // ------------------------------------------------------------- sessions

    private static void MapSessions(WebApplication app)
    {
        var group = app.MapGroup("/api/sessions").WithTags("Sessions");

        group.MapGet("/", async (SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.GetAsync(id, ct)));

        group.MapPost("/", async (CreateSessionRequest request, SessionService sessions, CancellationToken ct) =>
        {
            var result = await sessions.CreateAsync(request, CurrentUser.Id, ct);
            return result.Ok
                ? Results.Created($"/api/sessions/{result.Value!.Id}", result.Value)
                : Problems.From(result.Error!);
        });

        group.MapPost("/{id:guid}/start", async (Guid id, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.StartAsync(id, CurrentUser.Id, ct)));

        group.MapPost("/{id:guid}/finish", async (Guid id, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.FinishAsync(id, CurrentUser.Id, ct)));

        group.MapPost("/{id:guid}/reopen", async (Guid id, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.ReopenAsync(id, CurrentUser.Id, ct)));

        // Writes nothing. Called on every change in the console, so it has to
        // stay cheap.
        group.MapPost("/{id:guid}/scoring/preview",
            async (Guid id, PreviewRequest request, RoundService rounds, CancellationToken ct) =>
                Problems.Wrap(await rounds.PreviewAsync(id, request, ct)));

        group.MapPost("/{id:guid}/rounds",
            async (Guid id, CreateRoundRequest request, RoundService rounds, CancellationToken ct) =>
            {
                var result = await rounds.CreateAsync(id, request, CurrentUser.Id, ct);

                if (!result.Ok) return Problems.From(result.Error!);

                var (dto, wasReplay) = result.Value;

                // 200 for a replay, 201 for a round that was actually created.
                // A client retrying after a lost response can tell which
                // happened without guessing.
                return wasReplay
                    ? Results.Ok(dto)
                    : Results.Created($"/api/rounds/{dto.RoundId}", dto);
            });
    }

    // --------------------------------------------------------------- rounds

    private static void MapRounds(WebApplication app)
    {
        var group = app.MapGroup("/api/rounds").WithTags("Rounds");

        group.MapPut("/{id:guid}",
            async (Guid id, UpdateRoundRequest request, RoundService rounds, CancellationToken ct) =>
                Problems.Wrap(await rounds.UpdateAsync(id, request, CurrentUser.Id, ct)));

        group.MapPost("/{id:guid}/void",
            async (Guid id, VoidRoundRequest request, RoundService rounds, CancellationToken ct) =>
                Problems.Wrap(await rounds.VoidAsync(id, request.Reason, CurrentUser.Id, ct)));
    }
}

/// <summary>
/// Placeholder until Google sign-in lands on day 10, at which point this is
/// replaced by the authenticated principal. Kept in one place so there is
/// exactly one thing to change.
/// </summary>
internal static class CurrentUser
{
    public static Guid? Id => null;
}

/// <summary>Maps service failures onto RFC 9457 problem responses.</summary>
internal static class Problems
{
    public static IResult From(ServiceError error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error.Errors is { Count: > 0 })
        {
            extensions["errors"] = error.Errors;
        }

        return Results.Problem(
            detail: error.Message,
            statusCode: error.Status,
            title: TitleFor(error.Status),
            extensions: extensions);
    }

    public static IResult Wrap<T>(ServiceResult<T> result) =>
        result.Ok ? Results.Ok(result.Value) : From(result.Error!);

    private static string TitleFor(int status) => status switch
    {
        400 => "Invalid request",
        404 => "Not found",
        409 => "Conflict",
        _ => "Error",
    };
}
