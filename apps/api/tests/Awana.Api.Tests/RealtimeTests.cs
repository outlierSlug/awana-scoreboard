using System.Net.Http.Json;
using Awana.Api.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Awana.Api.Tests;

/// <summary>
/// The real-time layer is the product. A scoreboard that needs refreshing is
/// the paper clipboard with extra steps, so these exercise an actual hub
/// connection rather than trusting that the broadcaster was called.
/// </summary>
[Collection(nameof(ApiCollection))]
public class RealtimeTests(ApiFixture fixture)
{
    private HubConnection Connect() =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(fixture.Server.BaseAddress, "hubs/scoreboard"),
                options => options.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler())
            .Build();

    [Fact]
    public async Task Confirming_a_round_pushes_the_new_scoreboard_to_a_connected_board()
    {
        var client = fixture.CreateClient();
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var created = await client.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest(divisionId, new DateOnly(2026, 11, 6)));
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;
        await client.PostAsync($"/api/sessions/{session.Id}/start", null);

        await using var connection = Connect();

        var pushed = new TaskCompletionSource<ScoreboardDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Joining pushes current state first, so skip that one and wait for the
        // update caused by the round.
        var seenFirst = false;
        connection.On<ScoreboardDto>("ScoreboardUpdated", dto =>
        {
            if (!seenFirst) { seenFirst = true; return; }
            pushed.TrySetResult(dto);
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinSession", session.Slug);

        await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", new CreateRoundRequest(
            Guid.NewGuid(), gameId, 1m,
            teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList()));

        var completed = await Task.WhenAny(pushed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(completed == pushed.Task, "No scoreboard was pushed within 10 seconds.");

        var board = await pushed.Task;

        Assert.Equal(1, board.RoundCount);
        Assert.Equal(40m, board.Standings.Single(s => s.TeamId == teams[0]).Points);

        // The version is what lets a client discard a stale message that arrives
        // after a fresher one following a reconnect.
        Assert.True(board.Version > 0);
    }

    [Fact]
    public async Task Joining_immediately_returns_current_state_so_a_reconnect_needs_no_catch_up_path()
    {
        var client = fixture.CreateClient();
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var created = await client.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest(divisionId, new DateOnly(2026, 11, 13)));
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;
        await client.PostAsync($"/api/sessions/{session.Id}/start", null);

        // A round is recorded BEFORE anyone connects, which is what happens when
        // a board is opened late or its browser is restarted mid-session.
        await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", new CreateRoundRequest(
            Guid.NewGuid(), gameId, 1m,
            teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList()));

        await using var connection = Connect();

        var onJoin = new TaskCompletionSource<ScoreboardDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<ScoreboardDto>("ScoreboardUpdated", dto => onJoin.TrySetResult(dto));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinSession", session.Slug);

        var completed = await Task.WhenAny(onJoin.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(completed == onJoin.Task, "Joining did not push current state.");

        var board = await onJoin.Task;

        Assert.Equal(1, board.RoundCount);
        Assert.Equal(40m, board.Standings.Single(s => s.TeamId == teams[0]).Points);
    }

    [Fact]
    public async Task Joining_an_unknown_session_fails_rather_than_silently_watching_nothing()
    {
        await using var connection = Connect();
        await connection.StartAsync();

        await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync("JoinSession", "no-such-session"));
    }
}
