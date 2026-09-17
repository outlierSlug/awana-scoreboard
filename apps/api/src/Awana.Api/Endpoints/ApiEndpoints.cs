using System.Security.Claims;
using Awana.Api.Auth;
using Awana.Api.Contracts;
using Awana.Api.Services;
using Awana.Data;
using Awana.Scoring;
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
        MapGames(app);
        MapScoringProfiles(app);
        MapSessions(app);
        MapRounds(app);
        MapAdjustments(app);
        MapPeople(app);
    }

    // ----------------------------------------------------------------- people

    /// <summary>
    /// Who can sign in and what they may do, plus the record of what everyone
    /// has done. Admins only, the whole group: it is the one place in the API
    /// that lists other people's email addresses.
    /// </summary>
    private static void MapPeople(WebApplication app)
    {
        var people = app.MapGroup("/api/people")
            .WithTags("People")
            .RequireAuthorization(AuthPolicies.Admin);

        people.MapGet("/", async (ClaimsPrincipal user, PeopleService service, CancellationToken ct) =>
            user.Church() is { } church
                ? Results.Ok(await service.ListAsync(church, ct))
                : Problems.NoChurch());

        people.MapPost("/", async (
            AddPersonRequest request, ClaimsPrincipal user, PeopleService service, CancellationToken ct) =>
        {
            if (user.Church() is not { } church) return Problems.NoChurch();

            var result = await service.AddAsync(church, request, user.Id(), ct);
            return result.Ok
                ? Results.Created($"/api/people/{result.Value!.Id}", result.Value)
                : Problems.From(result.Error!);
        });

        people.MapPut("/{id:guid}/role", async (
            Guid id, SetRoleRequest request, ClaimsPrincipal user, PeopleService service, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await service.SetRoleAsync(church, id, request, user.Id(), ct))
                : Problems.NoChurch());

        // Actions rather than a flag in a payload, like retiring a game: the
        // two are different decisions with different consequences, and each
        // gets its own line in the audit log.
        people.MapPost("/{id:guid}/deactivate", async (
            Guid id, ClaimsPrincipal user, PeopleService service, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await service.SetActiveAsync(church, id, false, user.Id(), ct))
                : Problems.NoChurch());

        people.MapPost("/{id:guid}/reactivate", async (
            Guid id, ClaimsPrincipal user, PeopleService service, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await service.SetActiveAsync(church, id, true, user.Id(), ct))
                : Problems.NoChurch());

        app.MapGet("/api/activity", async (
                DateTimeOffset? before, int? limit, ClaimsPrincipal user, PeopleService service, CancellationToken ct) =>
                user.Church() is { } church
                    ? Results.Ok(await service.ActivityAsync(church, before, limit, ct))
                    : Problems.NoChurch())
            .WithTags("People")
            .RequireAuthorization(AuthPolicies.Admin);
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

            // prompt=select_account, every time, for two reasons.
            //
            // Without it Google silently reuses whichever account the browser
            // signed in with last. Someone whose address is not on the
            // allowlist is then stuck: they are refused, they press the button
            // again, and Google hands back the same rejected account with no
            // chooser and no way to reach one. The only escape is signing out
            // of Google entirely, which nobody guesses.
            //
            // The second reason outlasts that one. A round records who entered
            // it, and the console runs on a shared laptop, so an account
            // carried over silently from the last volunteer puts the wrong name
            // on tonight's rounds. Being asked which account is the point, not
            // a cost.
            return Results.Challenge(
                new GoogleChallengeProperties
                {
                    RedirectUri = ReturnTargets.AfterSignIn(context, returnUrl),
                    Prompt = "select_account",
                },
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
            if (user.Id() is not { } id) return Results.Ok(new MeDto(false, null, null, null, null));

            return Results.Ok(new MeDto(
                true,
                id,
                user.FindFirstValue(System.Security.Claims.ClaimTypes.Name),
                user.FindFirstValue(System.Security.Claims.ClaimTypes.Email),
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

        // Every division is offered every game. Which ones a division actually
        // plays on a given night is the games leader's call, not the picker's.
        group.MapGet("/games", async (AwanaDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Games
                .AsNoTracking()
                .Where(g => g.IsActive)
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Name)
                .Select(g => new GameDto(g.Id, g.Name))
                .ToListAsync(ct)));
    }

    // ---------------------------------------------------------- game catalog

    /// <summary>
    /// Editing the catalog, as opposed to reading it to record a round.
    /// </summary>
    /// <remarks>
    /// Its own group rather than more verbs on /api/games, because the audience
    /// is different: the picker wants the active games for one division and
    /// nothing else, while the catalog wants everything including what has been
    /// retired. One endpoint serving both would have to be told which it was
    /// being asked for on every call.
    ///
    /// A games leader owns this. It is the role named after the job, and
    /// somebody who can create and run a session can reasonably say what is
    /// being played in it. Delete stays with an admin for the same reason
    /// deleting a session does: it is the one action here that removes a row
    /// rather than hiding it.
    /// </remarks>
    private static void MapGames(WebApplication app)
    {
        var group = app.MapGroup("/api/catalog/games")
            .WithTags("Game catalog")
            .RequireAuthorization(AuthPolicies.GamesLeader);

        group.MapGet("/", async (ClaimsPrincipal user, GameService games, CancellationToken ct) =>
            user.Church() is { } church
                ? Results.Ok(await games.ListAsync(church, ct))
                : Problems.NoChurch());

        group.MapPost("/", async (
            SaveGameRequest request, ClaimsPrincipal user, GameService games, CancellationToken ct) =>
        {
            if (user.Church() is not { } church) return Problems.NoChurch();

            var result = await games.CreateAsync(church, request, user.Id(), ct);
            return result.Ok
                ? Results.Created($"/api/catalog/games/{result.Value!.Id}", result.Value)
                : Problems.From(result.Error!);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, SaveGameRequest request, ClaimsPrincipal user, GameService games, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await games.UpdateAsync(church, id, request, user.Id(), ct))
                : Problems.NoChurch());

        // Retiring is the ordinary way to get rid of a game, so it is a plain
        // action rather than a flag buried in the update payload.
        group.MapPost("/{id:guid}/retire", async (
            Guid id, ClaimsPrincipal user, GameService games, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await games.SetActiveAsync(church, id, false, user.Id(), ct))
                : Problems.NoChurch());

        group.MapPost("/{id:guid}/restore", async (
            Guid id, ClaimsPrincipal user, GameService games, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await games.SetActiveAsync(church, id, true, user.Id(), ct))
                : Problems.NoChurch());

        group.MapPut("/order", async (
            ReorderGamesRequest request, ClaimsPrincipal user, GameService games, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await games.ReorderAsync(church, request.Ids, user.Id(), ct))
                : Problems.NoChurch());

        // Only ever a game that was never played. See DeleteAsync for why
        // retiring is the answer for every other one.
        group.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, GameService games, CancellationToken ct) =>
        {
            if (user.Church() is not { } church) return Problems.NoChurch();

            var result = await games.DeleteAsync(church, id, user.Id(), ct);
            return result.Ok ? Results.NoContent() : Problems.From(result.Error!);
        })
            .RequireAuthorization(AuthPolicies.Admin);
    }

    // --------------------------------------------------------- scoring rules

    /// <summary>
    /// The named sets of scoring rules, and the editor behind them.
    /// </summary>
    /// <remarks>
    /// Reading is open to a scorekeeper. A games leader needs the list to choose
    /// which rules a session runs under, and a scorekeeper entering the rounds
    /// is the person most often asked why a tie scored the way it did, so they
    /// should be able to open the rules and see. Writing is an admin's, because
    /// these decide what every future night is worth and one wrong digit here
    /// is worth more than any single mistyped round.
    ///
    /// Editing is safe for history regardless: a session freezes its own copy
    /// of the rules when it starts, so nothing here can reach a night already
    /// played. See ScoringProfileService.
    /// </remarks>
    private static void MapScoringProfiles(WebApplication app)
    {
        var group = app.MapGroup("/api/catalog/scoring-profiles")
            .WithTags("Scoring rules")
            .RequireAuthorization(AuthPolicies.Scorekeeper);

        group.MapGet("/", async (ClaimsPrincipal user, ScoringProfileService profiles, CancellationToken ct) =>
            user.Church() is { } church
                ? Results.Ok(await profiles.ListAsync(church, ct))
                : Problems.NoChurch());

        // Nothing is written and no profile need exist, so the editor can show
        // what an unsaved edit would do before anyone commits to it. A games
        // leader can reach it for the same reason they can read the list.
        group.MapPost("/preview", (ScoringConfig config, ScoringProfileService profiles) =>
            Problems.Wrap(profiles.Preview(config)));

        var admin = group.MapGroup("/").RequireAuthorization(AuthPolicies.Admin);

        admin.MapPost("/", async (
            SaveScoringProfileRequest request, ClaimsPrincipal user,
            ScoringProfileService profiles, CancellationToken ct) =>
        {
            if (user.Church() is not { } church) return Problems.NoChurch();

            var result = await profiles.CreateAsync(church, request, user.Id(), ct);
            return result.Ok
                ? Results.Created($"/api/catalog/scoring-profiles/{result.Value!.Id}", result.Value)
                : Problems.From(result.Error!);
        });

        // How anything gets changed about the standard table, which is read
        // only: copy it, then edit the copy.
        admin.MapPost("/{id:guid}/duplicate", async (
            Guid id, ClaimsPrincipal user, ScoringProfileService profiles, CancellationToken ct) =>
        {
            if (user.Church() is not { } church) return Problems.NoChurch();

            var result = await profiles.DuplicateAsync(church, id, user.Id(), ct);
            return result.Ok
                ? Results.Created($"/api/catalog/scoring-profiles/{result.Value!.Id}", result.Value)
                : Problems.From(result.Error!);
        });

        admin.MapPut("/{id:guid}", async (
            Guid id, SaveScoringProfileRequest request, ClaimsPrincipal user,
            ScoringProfileService profiles, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await profiles.UpdateAsync(church, id, request, user.Id(), ct))
                : Problems.NoChurch());

        // What a new session gets when nobody chooses.
        admin.MapPost("/{id:guid}/default", async (
            Guid id, ClaimsPrincipal user, ScoringProfileService profiles, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await profiles.SetDefaultAsync(church, id, user.Id(), ct))
                : Problems.NoChurch());

        admin.MapPost("/{id:guid}/retire", async (
            Guid id, ClaimsPrincipal user, ScoringProfileService profiles, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await profiles.SetActiveAsync(church, id, false, user.Id(), ct))
                : Problems.NoChurch());

        admin.MapPost("/{id:guid}/restore", async (
            Guid id, ClaimsPrincipal user, ScoringProfileService profiles, CancellationToken ct) =>
            user.Church() is { } church
                ? Problems.Wrap(await profiles.SetActiveAsync(church, id, true, user.Id(), ct))
                : Problems.NoChurch());

        // Only ever a set of rules no session has used. See DeleteAsync.
        admin.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, ScoringProfileService profiles, CancellationToken ct) =>
        {
            if (user.Church() is not { } church) return Problems.NoChurch();

            var result = await profiles.DeleteAsync(church, id, user.Id(), ct);
            return result.Ok ? Results.NoContent() : Problems.From(result.Error!);
        });
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

        // Only while the session is still in setup. See SetScoringAsync.
        group.MapPut("/{id:guid}/scoring", async (
            Guid id, SetSessionScoringRequest request, ClaimsPrincipal user,
            SessionService sessions, CancellationToken ct) =>
            Problems.Wrap(await sessions.SetScoringAsync(id, request.ScoringProfileId, user.Id(), ct)))
            .RequireAuthorization(AuthPolicies.GamesLeader);

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

        // Only ever a session that never happened, and only an admin's call.
        // See DeleteAsync for why this is not the way to undo a night.
        group.MapDelete("/{id:guid}",
            async (Guid id, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            {
                var result = await sessions.DeleteAsync(id, user.Id(), ct);
                return result.Ok ? Results.NoContent() : Problems.From(result.Error!);
            })
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

    /// <summary>
    /// A signed-in caller whose cookie carries no church.
    /// </summary>
    /// <remarks>
    /// Only reachable with a cookie issued before the claim existed, so the
    /// answer is 401 rather than 500: signing in again fixes it, and that is
    /// what a 401 tells the web app to offer.
    /// </remarks>
    public static IResult NoChurch() => Results.Problem(
        detail: "Your sign-in is out of date. Sign in again.",
        statusCode: 401,
        title: "Not signed in",
        extensions: new Dictionary<string, object?> { ["code"] = "no_church" });

    private static string TitleFor(int status) => status switch
    {
        400 => "Invalid request",
        404 => "Not found",
        409 => "Conflict",
        _ => "Error",
    };
}
