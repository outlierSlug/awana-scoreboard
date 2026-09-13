using System.Net;
using System.Net.Http.Json;
using Awana.Api.Contracts;

namespace Awana.Api.Tests;

/// <summary>
/// Each of these asserts behavior that was missing or broken at some point.
/// The first four came from defects in the previous version.
/// </summary>
[Collection(nameof(ApiCollection))]
public class RoundLifecycleTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.CreateClient();

    private static CreateRoundRequest CleanRound(Guid gameId, IReadOnlyList<Guid> teams, Guid? requestId = null) =>
        new(
            ClientRequestId: requestId ?? Guid.NewGuid(),
            GameId: gameId,
            Multiplier: 1m,
            Entries: teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList());

    private async Task<SessionDetailDto> CreateSessionAsync(HttpClient client, Guid divisionId, DateOnly date)
    {
        var response = await client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(divisionId, date));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionDetailDto>())!;
    }

    private static async Task StartAsync(HttpClient client, Guid sessionId) =>
        (await client.PostAsync($"/api/sessions/{sessionId}/start", null)).EnsureSuccessStatusCode();

    // ------------------------------------------------------------------- 1

    [Fact]
    public async Task Posting_a_round_to_a_finished_session_is_rejected()
    {
        // v1 wrote a session-status guard and never registered it, so a round
        // could be recorded against a session that was already over.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 10, 2));
        await StartAsync(client, session.Id);
        (await client.PostAsync($"/api/sessions/{session.Id}/finish", null)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/rounds", CleanRound(gameId, teams));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("session_not_running", problem!["code"].ToString());
    }

    // ------------------------------------------------------------------- 2

    [Fact]
    public async Task Replaying_the_same_request_id_returns_the_same_round_and_does_not_score_twice()
    {
        // v1 had nothing here. On church wifi a POST can succeed with its
        // response lost, and the retry would score the round a second time.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 10, 9));
        await StartAsync(client, session.Id);

        var requestId = Guid.NewGuid();
        var request = CleanRound(gameId, teams, requestId);

        var first = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", request);
        var second = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // 200 rather than 201, so a client can tell a replay from a new round.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var a = (await first.Content.ReadFromJsonAsync<RoundRecordedDto>())!;
        var b = (await second.Content.ReadFromJsonAsync<RoundRecordedDto>())!;

        Assert.Equal(a.RoundId, b.RoundId);
        Assert.Equal(1, b.Scoreboard.RoundCount);

        // The decisive assertion: the winner still has 40, not 80.
        Assert.Equal(40m, b.Scoreboard.Standings.Single(s => s.TeamId == teams[0]).Points);
    }

    // ------------------------------------------------------------------- 3

    [Fact]
    public async Task Voiding_a_round_recomputes_totals_for_every_team_including_those_absent_from_it()
    {
        // THE v1 bug. Its recompute seeded the team map only from the deleted
        // round's results, so any team not in that round kept a stale total.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 10, 16));
        await StartAsync(client, session.Id);

        // Round 1: all four teams score.
        await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", CleanRound(gameId, teams));

        // Round 2: only the first two teams take part. The other two are absent.
        var partial = new CreateRoundRequest(
            Guid.NewGuid(), gameId, 1m,
            [
                new RoundEntryRequest(teams[0], 1, false, 0m, null),
                new RoundEntryRequest(teams[1], 2, false, 0m, null),
                new RoundEntryRequest(teams[2], null, false, 0m, null),
                new RoundEntryRequest(teams[3], null, false, 0m, null),
            ]);

        var second = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", partial);
        var recorded = (await second.Content.ReadFromJsonAsync<RoundRecordedDto>())!;

        Assert.Equal(80m, recorded.Scoreboard.Standings.Single(s => s.TeamId == teams[0]).Points);
        Assert.Equal(20m, recorded.Scoreboard.Standings.Single(s => s.TeamId == teams[2]).Points);

        // Void round 1, the one the absent teams DID play in.
        var firstRoundId = (await client.GetFromJsonAsync<SessionDetailDto>($"/api/sessions/{session.Id}"))!
            .Rounds.Single(r => r.RoundNumber == 1).Id;

        var voidResponse = await client.PostAsJsonAsync(
            $"/api/rounds/{firstRoundId}/void", new VoidRoundRequest("False start"));

        voidResponse.EnsureSuccessStatusCode();
        var board = (await voidResponse.Content.ReadFromJsonAsync<ScoreboardDto>())!;

        // Teams 0 and 1 keep only their round 2 points.
        Assert.Equal(40m, board.Standings.Single(s => s.TeamId == teams[0]).Points);
        Assert.Equal(30m, board.Standings.Single(s => s.TeamId == teams[1]).Points);

        // Teams 2 and 3 were NOT in round 2 at all. Their round 1 points must
        // still be removed, and they must drop to zero rather than keeping a
        // stale 20 and 10.
        Assert.Equal(0m, board.Standings.Single(s => s.TeamId == teams[2]).Points);
        Assert.Equal(0m, board.Standings.Single(s => s.TeamId == teams[3]).Points);

        Assert.Equal(1, board.RoundCount);
    }

    // ------------------------------------------------------------------- 4

    [Fact]
    public async Task The_public_scoreboard_is_readable_without_signing_in()
    {
        // The board is a URL typed into a TV. If this ever starts requiring
        // authentication, the product stops working.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 10, 23));
        await StartAsync(client, session.Id);
        await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", CleanRound(gameId, teams));

        var response = await client.GetAsync($"/api/public/sessions/{session.Slug}/scoreboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var board = (await response.Content.ReadFromJsonAsync<ScoreboardDto>())!;
        Assert.Equal(4, board.Standings.Count);
        Assert.Equal(40m, board.Standings[0].Points);
    }

    // ------------------------------------------------------------------- 5

    [Fact]
    public async Task Editing_a_round_rescores_it_in_place()
    {
        // A correction has to keep the round's identity. Voiding and recording
        // a replacement renumbers the night and loses the round if the
        // scorekeeper backs out halfway, so the console edits in place.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 10, 30));
        await StartAsync(client, session.Id);

        var created = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/rounds", CleanRound(gameId, teams));
        var before = (await created.Content.ReadFromJsonAsync<RoundRecordedDto>())!;

        // The same four teams in the opposite order, which is the common
        // correction: the places were entered backwards.
        var reversed = new UpdateRoundRequest(
            GameId: gameId,
            Multiplier: 2m,
            Entries: Enumerable.Reverse(teams).Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList());

        var response = await client.PutAsJsonAsync($"/api/rounds/{before.RoundId}", reversed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = (await response.Content.ReadFromJsonAsync<RoundRecordedDto>())!;

        Assert.Equal(before.RoundId, after.RoundId);
        Assert.Equal(before.RoundNumber, after.RoundNumber);

        // Rescored, not appended: one result per team, at the new multiplier.
        Assert.Equal(4, after.Awards.Count);
        Assert.Equal(80m, after.Awards.Single(a => a.TeamId == teams[3]).Points);
        Assert.Equal(20m, after.Awards.Single(a => a.TeamId == teams[0]).Points);

        // And the standings agree, so no stale result rows were left behind.
        Assert.Equal(80m, after.Scoreboard.Standings.Single(s => s.TeamId == teams[3]).Points);
        Assert.Equal(20m, after.Scoreboard.Standings.Single(s => s.TeamId == teams[0]).Points);
        Assert.Equal(1, after.Scoreboard.RoundCount);
    }
}
