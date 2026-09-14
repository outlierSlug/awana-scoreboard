using System.Net;
using System.Net.Http.Json;
using Awana.Api.Contracts;
using Awana.Data.Entities;

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

    // ------------------------------------------------------------------- 6

    [Fact]
    public async Task Recording_a_round_requires_signing_in()
    {
        // The console is a URL like any other. Before auth existed, anybody who
        // found it could record, correct and clear rounds.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 11, 6));
        await StartAsync(client, session.Id);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sessions/{session.Id}/rounds")
        {
            Content = JsonContent.Create(CleanRound(gameId, teams)),
        };
        request.Headers.Add(TestAuth.RoleHeader, TestAuth.Anonymous);

        var response = await client.SendAsync(request);

        // 401 and not a redirect. Once Google was configured it became the
        // default challenge, and an ordinary API call started answering with a
        // 302 to accounts.google.com: the browser follows it, CORS refuses it,
        // and what should have been a plain 401 surfaces as a network error.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------------- 7

    [Fact]
    public async Task Reopening_a_finished_session_is_kept_to_an_admin()
    {
        // Recording rounds and rewriting a night that was already called
        // finished are different levels of trust, and the roles are ordered so
        // that the second implies the first rather than the other way round.
        var client = Client;
        var (divisionId, _, _) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 11, 13));
        await StartAsync(client, session.Id);
        (await client.PostAsync($"/api/sessions/{session.Id}/finish", null)).EnsureSuccessStatusCode();

        using var asScorekeeper = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sessions/{session.Id}/reopen");
        asScorekeeper.Headers.Add(TestAuth.RoleHeader, nameof(UserRole.Scorekeeper));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(asScorekeeper)).StatusCode);

        // And the admin the role exists for can.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/sessions/{session.Id}/reopen", null)).StatusCode);
    }

    // ------------------------------------------------------------------- 8

    [Fact]
    public async Task A_round_in_a_finished_session_cannot_be_edited_or_cleared()
    {
        // The console hides both once a session is finished, but the console is
        // not the gate. A stale tab, or anything else holding the round's id,
        // could rewrite the standings the room was already sent home with.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 11, 20));
        await StartAsync(client, session.Id);

        var created = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/rounds", CleanRound(gameId, teams));
        var round = (await created.Content.ReadFromJsonAsync<RoundRecordedDto>())!;

        (await client.PostAsync($"/api/sessions/{session.Id}/finish", null)).EnsureSuccessStatusCode();

        var edit = new UpdateRoundRequest(
            GameId: gameId,
            Multiplier: 2m,
            Entries: Enumerable.Reverse(teams).Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList());

        var edited = await client.PutAsJsonAsync($"/api/rounds/{round.RoundId}", edit);
        var cleared = await client.PostAsJsonAsync(
            $"/api/rounds/{round.RoundId}/void", new VoidRoundRequest("changed my mind"));

        Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cleared.StatusCode);

        // And the standings are untouched by either attempt.
        var board = (await client.GetFromJsonAsync<ScoreboardDto>(
            $"/api/public/sessions/{session.Slug}/scoreboard"))!;
        Assert.Equal(40m, board.Standings.Single(s => s.TeamId == teams[0]).Points);
    }

    // ------------------------------------------------------------------- 9

    [Fact]
    public async Task Bad_input_is_answered_rather_than_crashed_on()
    {
        // Each of these used to reach the database and come back as a 500: a
        // request that was wrong in an ordinary, explainable way, reported as
        // the server breaking. The scorekeeper sees "something went wrong" and
        // has nothing to act on.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 11, 27));
        await StartAsync(client, session.Id);

        var entries = teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList();

        var unknownGame = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/rounds",
            new CreateRoundRequest(Guid.NewGuid(), Guid.NewGuid(), 1m, entries));

        Assert.Equal(HttpStatusCode.NotFound, unknownGame.StatusCode);

        var recorded = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/rounds", CleanRound(gameId, teams));
        var round = (await recorded.Content.ReadFromJsonAsync<RoundRecordedDto>())!;

        var longReason = await client.PostAsJsonAsync(
            $"/api/rounds/{round.RoundId}/void", new VoidRoundRequest(new string('x', 5000)));

        Assert.Equal(HttpStatusCode.BadRequest, longReason.StatusCode);

        // And an empty one, since the reason is the only record of what happened.
        var noReason = await client.PostAsJsonAsync(
            $"/api/rounds/{round.RoundId}/void", new VoidRoundRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        // The round survived both attempts.
        var board = (await client.GetFromJsonAsync<ScoreboardDto>(
            $"/api/public/sessions/{session.Slug}/scoreboard"))!;
        Assert.Equal(1, board.RoundCount);
    }

    // ------------------------------------------------------------------ 10

    [Fact]
    public async Task An_adjustment_moves_the_standings_and_can_be_taken_back()
    {
        // The read path already summed these into the standings; there was
        // simply no way to create one. This is the last of the four
        // corrections: a leader decides something outside the games.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 12, 4));
        await StartAsync(client, session.Id);
        await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", CleanRound(gameId, teams));

        // Last place, so the award is visible rather than lost in the noise.
        var awarded = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/adjustments",
            new CreateAdjustmentRequest(teams[3], 25m, "Sportsmanship"));

        Assert.Equal(HttpStatusCode.OK, awarded.StatusCode);

        var board = (await awarded.Content.ReadFromJsonAsync<ScoreboardDto>())!;
        Assert.Equal(35m, board.Standings.Single(s => s.TeamId == teams[3]).Points);

        // It shows up on the session, and clearing it puts the total back.
        var detail = (await client.GetFromJsonAsync<SessionDetailDto>($"/api/sessions/{session.Id}"))!;
        var adjustment = Assert.Single(detail.Adjustments);
        Assert.Equal("Sportsmanship", adjustment.Reason);

        var cleared = await client.PostAsync($"/api/adjustments/{adjustment.Id}/void", null);
        var after = (await cleared.Content.ReadFromJsonAsync<ScoreboardDto>())!;

        Assert.Equal(10m, after.Standings.Single(s => s.TeamId == teams[3]).Points);
    }

    // ------------------------------------------------------------------ 11

    [Fact]
    public async Task An_adjustment_has_to_say_what_it_was_for()
    {
        // An unexplained adjustment is indistinguishable from a bug, which is
        // the whole reason these are kept apart from round results.
        var client = Client;
        var (divisionId, _, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 12, 11));
        await StartAsync(client, session.Id);

        var noReason = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/adjustments", new CreateAdjustmentRequest(teams[0], 10m, "  "));
        var noPoints = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/adjustments", new CreateAdjustmentRequest(teams[0], 0m, "Nothing"));
        var strangerTeam = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/adjustments", new CreateAdjustmentRequest(Guid.NewGuid(), 10m, "Who"));

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noPoints.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, strangerTeam.StatusCode);
    }

    // ------------------------------------------------------------------ 12

    [Fact]
    public async Task Headcounts_are_optional_and_bounded()
    {
        var client = Client;
        var (divisionId, _, teams) = await fixture.FixtureIdsAsync();

        var session = await CreateSessionAsync(client, divisionId, new DateOnly(2026, 12, 18));
        await StartAsync(client, session.Id);

        var saved = await client.PutAsJsonAsync(
            $"/api/sessions/{session.Id}/attendance",
            new UpdateAttendanceRequest([new TeamHeadcount(teams[0], 12), new TeamHeadcount(teams[1], null)]));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var detail = (await saved.Content.ReadFromJsonAsync<SessionDetailDto>())!;
        Assert.Equal(12, detail.Teams.Single(t => t.TeamId == teams[0]).Headcount);
        Assert.Null(detail.Teams.Single(t => t.TeamId == teams[1]).Headcount);

        var absurd = await client.PutAsJsonAsync(
            $"/api/sessions/{session.Id}/attendance",
            new UpdateAttendanceRequest([new TeamHeadcount(teams[0], -5)]));

        Assert.Equal(HttpStatusCode.BadRequest, absurd.StatusCode);
    }

    // ------------------------------------------------------------------ 13

    [Fact]
    public async Task A_session_can_be_deleted_only_while_it_holds_nothing()
    {
        // Session to round is a cascade, so deleting a played session would
        // erase the night rather than correct it. Everything else here is
        // retired or voided for exactly that reason.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var empty = await CreateSessionAsync(client, divisionId, new DateOnly(2027, 1, 8));
        var played = await CreateSessionAsync(client, divisionId, new DateOnly(2027, 1, 15));

        await StartAsync(client, played.Id);
        await client.PostAsJsonAsync($"/api/sessions/{played.Id}/rounds", CleanRound(gameId, teams));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/sessions/{empty.Id}")).StatusCode);

        var refused = await client.DeleteAsync($"/api/sessions/{played.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // And once every round has been cleared it can go, because nothing
        // stands against it any more. Counting cleared rounds here and nowhere
        // else told somebody looking at a session with no rounds that it had
        // rounds, which is a rule they cannot see the inputs to.
        var detail = (await client.GetFromJsonAsync<SessionDetailDto>($"/api/sessions/{played.Id}"))!;
        foreach (var round in detail.Rounds)
        {
            await client.PostAsJsonAsync(
                $"/api/rounds/{round.Id}/void", new VoidRoundRequest("Recorded by mistake"));
        }

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/sessions/{played.Id}")).StatusCode);

        // And a scorekeeper cannot delete even the empty kind.
        var another = await CreateSessionAsync(client, divisionId, new DateOnly(2027, 1, 22));
        using var asScorekeeper = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/sessions/{another.Id}");
        asScorekeeper.Headers.Add(TestAuth.RoleHeader, nameof(UserRole.Scorekeeper));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(asScorekeeper)).StatusCode);
    }
}
