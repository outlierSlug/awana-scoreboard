using System.Security.Claims;
using Awana.Api.Auth;
using Awana.Api.Contracts;
using Awana.Api.Services;
using Awana.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Endpoints;

public static class ApiEndpoints
{
    public static void MapAwanaApi(this WebApplication app)
    {
        MapAuth(app);
        MapPublic(app);
        MapCatalog(app);
        MapSessions(app);
        MapRounds(app);
        MapAdjustments(app);
    }

    // ----------------------------------------------------------------- auth

    private static void MapAuth(WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        // A full page navigation, not a fetch: this hands the browser to
        // Google and Google hands it back, and neither leg can happen inside
        // an XHR.
        group.MapGet("/login", (HttpContext context, string? returnUrl) =>
        {
            var google = context.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();

            if (!google.IsConfigured)
            {
                return Results.Problem(
                    detail: "Google sign-in is not configured on this server.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Sign-in unavailable",
                    extensions: new Dictionary<string, object?> { ["code"] = "google_not_configured" });
            }

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = ReturnTargets.AfterSignIn(context, returnUrl) },
                [GoogleDefaults.AuthenticationScheme]);
        });

        group.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { returnUrl = ReturnTargets.AfterSignOut(context) });
        });

        // Who am I, if anyone. The web app asks this on load to decide between
        // the console and the sign-in page, so an anonymous caller is a normal
        // answer rather than a 401.
        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Id() is not { } id) return Results.Ok(new MeDto(false, null, null, null));

            return Results.Ok(new MeDto(
                true,
                id,
                user.FindFirstValue(System.Security.Claims.ClaimTypes.Name),
                user.RoleOf()?.ToString()));
        });
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
        // Divisions and the game list are only of use to somebody recording a
        // round, and naming the club's divisions is not public information.
        var group = app.MapGroup("/api").WithTags("Catalog").RequireAuthorization();

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
        // Reading a session is for anyone signed in. Changing one takes a
        // role, applied per endpoint below.
        var group = app.MapGroup("/api/sessions").WithTags("Sessions").RequireAuthorization();

        group.MapGet("/", async (SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.GetAsync(id, ct)));

        group.MapPost("/", async (CreateSessionRequest request, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
        {
            var result = await sessions.CreateAsync(request, user.Id(), ct);
            return result.Ok
                ? Results.Created($"/api/sessions/{result.Value!.Id}", result.Value)
                : Problems.From(result.Error!);
        }).RequireAuthorization(AuthPolicies.GamesLeader);

        group.MapPost("/{id:guid}/start", async (Guid id, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.StartAsync(id, user.Id(), ct)))
            .RequireAuthorization(AuthPolicies.GamesLeader);

        group.MapPost("/{id:guid}/finish", async (Guid id, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.FinishAsync(id, user.Id(), ct)))
            .RequireAuthorization(AuthPolicies.GamesLeader);

        group.MapPost("/{id:guid}/reopen", async (Guid id, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.ReopenAsync(id, user.Id(), ct)))
            // Reopening rewrites a night that was called finished, so it is the
            // one session action kept to an admin.
            .RequireAuthorization(AuthPolicies.Admin);

        // Points a leader decided on, outside the games. The scorekeeper is
        // the one at the console when a leader announces one, and every
        // adjustment carries their name and a written reason.
        group.MapPost("/{id:guid}/adjustments",
            async (Guid id, CreateAdjustmentRequest request, ClaimsPrincipal user,
                   SessionService sessions, CancellationToken ct) =>
                Problems.Wrap(await sessions.AddAdjustmentAsync(id, request, user.Id(), ct)))
            .RequireAuthorization(AuthPolicies.Scorekeeper);

        // A number per team and nothing else. Never a name: this is a
        // scoreboard, not an attendance system.
        group.MapPut("/{id:guid}/attendance",
            async (Guid id, UpdateAttendanceRequest request, ClaimsPrincipal user,
                   SessionService sessions, CancellationToken ct) =>
                Problems.Wrap(await sessions.UpdateAttendanceAsync(id, request, user.Id(), ct)))
            .RequireAuthorization(AuthPolicies.Scorekeeper);

        // Writes nothing. Called on every change in the console, so it has to
        // stay cheap.
        group.MapPost("/{id:guid}/scoring/preview",
            async (Guid id, PreviewRequest request, RoundService rounds, CancellationToken ct) =>
                Problems.Wrap(await rounds.PreviewAsync(id, request, ct)));

        group.MapPost("/{id:guid}/rounds",
            async (Guid id, CreateRoundRequest request, ClaimsPrincipal user, RoundService rounds, CancellationToken ct) =>
            {
                var result = await rounds.CreateAsync(id, request, user.Id(), ct);

                if (!result.Ok) return Problems.From(result.Error!);

                var (dto, wasReplay) = result.Value;

                // 200 for a replay, 201 for a round that was actually created.
                // A client retrying after a lost response can tell which
                // happened without guessing.
                return wasReplay
                    ? Results.Ok(dto)
                    : Results.Created($"/api/rounds/{dto.RoundId}", dto);
            }).RequireAuthorization(AuthPolicies.Scorekeeper);
    }

    // --------------------------------------------------------------- rounds

    private static void MapRounds(WebApplication app)
    {
        // Correcting a round is the scorekeeper's own job, same as recording one.
        var group = app.MapGroup("/api/rounds").WithTags("Rounds")
            .RequireAuthorization(AuthPolicies.Scorekeeper);

        group.MapPut("/{id:guid}",
            async (Guid id, UpdateRoundRequest request, ClaimsPrincipal user, RoundService rounds, CancellationToken ct) =>
                Problems.Wrap(await rounds.UpdateAsync(id, request, user.Id(), ct)));

        group.MapPost("/{id:guid}/void",
            async (Guid id, VoidRoundRequest request, ClaimsPrincipal user, RoundService rounds, CancellationToken ct) =>
                Problems.Wrap(await rounds.VoidAsync(id, request.Reason, user.Id(), ct)));
    }

    // ---------------------------------------------------------- adjustments

    private static void MapAdjustments(WebApplication app)
    {
        var group = app.MapGroup("/api/adjustments").WithTags("Adjustments")
            .RequireAuthorization(AuthPolicies.Scorekeeper);

        group.MapPost("/{id:guid}/void",
            async (Guid id, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
                Problems.Wrap(await sessions.VoidAdjustmentAsync(id, user.Id(), ct)));
    }
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
