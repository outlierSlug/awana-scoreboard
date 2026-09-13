using Awana.Api.Contracts;
using Awana.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Awana.Api.Realtime;

/// <summary>
/// Pushes scoreboard updates to whoever is watching.
///
/// BROADCAST ONLY. There is deliberately no method here that changes anything.
/// Every mutation goes through the REST endpoints, which already carry the
/// authorization, validation, status guards, idempotency and error shapes. A
/// second write path would be a second place to forget the status guard, which
/// is precisely the defect this rewrite exists to avoid.
/// </summary>
public class ScoreboardHub(ScoreboardService scoreboard) : Hub
{
    /// <summary>Server to client message names, in one place so they cannot drift.</summary>
    public static class Events
    {
        public const string ScoreboardUpdated = "ScoreboardUpdated";
        public const string SessionStatusChanged = "SessionStatusChanged";
    }

    /// <summary>
    /// Group key for a session. Keyed on the GUID rather than the slug, so
    /// renaming a slug cannot orphan a board that is already connected.
    /// </summary>
    public static string GroupFor(Guid sessionId) => $"session:{sessionId}";

    /// <summary>
    /// Anonymous on purpose: the board is a public URL with no sign-in.
    ///
    /// Joining immediately pushes current state, which means joining IS
    /// resyncing. A client that has just reconnected does not need a separate
    /// catch-up path, and it must call this again after reconnecting because
    /// group membership does not survive.
    /// </summary>
    public async Task JoinSession(string slugOrId)
    {
        var dto = Guid.TryParse(slugOrId, out var id)
            ? await scoreboard.BuildAsync(id, Context.ConnectionAborted)
            : await scoreboard.BuildBySlugAsync(slugOrId, Context.ConnectionAborted);

        if (dto is null)
        {
            throw new HubException($"No session matches '{slugOrId}'.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(dto.SessionId));
        await Clients.Caller.SendAsync(Events.ScoreboardUpdated, dto);
    }

    public async Task LeaveSession(Guid sessionId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(sessionId));
}

/// <summary>
/// How the REST endpoints push. Injected as an interface so a handler can be
/// tested without a live hub.
/// </summary>
public interface IScoreboardBroadcaster
{
    Task ScoreboardUpdatedAsync(ScoreboardDto scoreboard, CancellationToken ct = default);
    Task SessionStatusChangedAsync(ScoreboardDto scoreboard, CancellationToken ct = default);
}

public class ScoreboardBroadcaster(IHubContext<ScoreboardHub> hub) : IScoreboardBroadcaster
{
    public Task ScoreboardUpdatedAsync(ScoreboardDto scoreboard, CancellationToken ct = default) =>
        hub.Clients
            .Group(ScoreboardHub.GroupFor(scoreboard.SessionId))
            .SendAsync(ScoreboardHub.Events.ScoreboardUpdated, scoreboard, ct);

    public Task SessionStatusChangedAsync(ScoreboardDto scoreboard, CancellationToken ct = default) =>
        hub.Clients
            .Group(ScoreboardHub.GroupFor(scoreboard.SessionId))
            .SendAsync(ScoreboardHub.Events.SessionStatusChanged, scoreboard, ct);
}
