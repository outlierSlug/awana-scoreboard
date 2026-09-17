using System.Net;
using System.Net.Http.Json;
using Awana.Api.Contracts;
using Awana.Data.Entities;
using Awana.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Tests;

/// <summary>
/// Scoring rules are the one thing here that decides what every future night is
/// worth, so what these pin down is that editing them is safe for history, that
/// a church can never be left without a usable default, and that the preview
/// tells the truth about what Friday will do.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ScoringProfileTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.CreateClient();

    private const string Profiles = "/api/catalog/scoring-profiles";

    private static ScoringConfig Config(params decimal[] places) =>
        new() { PlacePoints = places };

    private async Task<ScoringProfileDto> CreateAsync(HttpClient client, string name, ScoringConfig config)
    {
        var response = await client.PostAsJsonAsync(
            Profiles, new SaveScoringProfileRequest(name, config));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ScoringProfileDto>())!;
    }

    // ------------------------------------------------------------------- 1

    [Fact]
    public async Task The_seeded_church_has_one_default_set_of_rules()
    {
        var profiles = await Client.GetFromJsonAsync<List<ScoringProfileDto>>(Profiles);

        Assert.NotNull(profiles);

        var official = Assert.Single(profiles, p => p.Name == "Official AWANA");
        Assert.True(official.IsActive);
        Assert.Equal<decimal[]>([40m, 30m, 20m, 10m], [.. official.Config.PlacePoints]);

        // Exactly one default, always. A church with none cannot start a session.
        Assert.Equal(1, profiles.Count(p => p.IsDefault));
    }

    // ------------------------------------------------------------------- 2

    [Fact]
    public async Task Editing_rules_cannot_change_a_session_that_has_already_started()
    {
        // The reason a session freezes its own copy. If this ever fails, an
        // admin tidying the profile in December rewrites October's scores.
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var profile = await CreateAsync(client, "Frozen Test", Config(40m, 30m, 20m, 10m));

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 1, 8), profile.Id));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;

        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        var round = new CreateRoundRequest(
            Guid.NewGuid(), gameId, 1m,
            teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList());

        var recorded = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", round);
        recorded.EnsureSuccessStatusCode();
        var before = (await recorded.Content.ReadFromJsonAsync<RoundRecordedDto>())!;
        var winnerBefore = before.Awards.Single(a => a.Place == 1).Points;

        Assert.Equal(40m, winnerBefore);

        // Now double the table underneath it.
        var edit = await client.PutAsJsonAsync(
            $"{Profiles}/{profile.Id}",
            new SaveScoringProfileRequest("Frozen Test", Config(80m, 60m, 40m, 20m)));
        edit.EnsureSuccessStatusCode();

        // The recorded round is untouched, and so is the board.
        var board = await client.GetFromJsonAsync<ScoreboardDto>(
            $"/api/public/sessions/{session.Slug}/scoreboard");

        Assert.Equal(40m, board!.Standings.Single(s => s.TeamId == teams[0]).Points);

        // Loaded whole rather than projected: the config is a jsonb document
        // behind a value converter, and EF cannot reach inside it in SQL.
        var frozen = await fixture.WithDbAsync(db => db.Sessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == session.Id));

        Assert.Equal<decimal[]>([40m, 30m, 20m, 10m], [.. frozen.ScoringConfig!.PlacePoints]);
    }

    // ------------------------------------------------------------------- 3

    [Fact]
    public async Task A_session_runs_on_the_rules_it_was_created_with()
    {
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var doubled = await CreateAsync(client, "Double Points", Config(80m, 60m, 40m, 20m));

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 1, 15), doubled.Id));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;

        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        var round = new CreateRoundRequest(
            Guid.NewGuid(), gameId, 1m,
            teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList());

        var recorded = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", round);
        recorded.EnsureSuccessStatusCode();
        var result = (await recorded.Content.ReadFromJsonAsync<RoundRecordedDto>())!;

        // Not the default's 40.
        Assert.Equal(80m, result.Awards.Single(a => a.Place == 1).Points);
    }

    // ------------------------------------------------------------------- 4

    [Fact]
    public async Task A_session_created_without_a_choice_gets_the_default()
    {
        var client = Client;
        var (divisionId, _, _) = await fixture.FixtureIdsAsync();

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 1, 22)));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;

        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        var defaultName = await fixture.WithDbAsync(db => db.Sessions
            .Where(s => s.Id == session.Id)
            .Select(s => s.ScoringProfile!.Name)
            .FirstAsync());

        Assert.Equal("Official AWANA", defaultName);
    }

    // ------------------------------------------------------------------- 5

    [Fact]
    public async Task Rules_that_cannot_be_executed_are_refused()
    {
        var client = Client;

        var empty = await client.PostAsJsonAsync(
            Profiles, new SaveScoringProfileRequest("Empty", Config()));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var negative = await client.PostAsJsonAsync(
            Profiles, new SaveScoringProfileRequest("Negative", Config(40m, -30m)));
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var nameless = await client.PostAsJsonAsync(
            Profiles, new SaveScoringProfileRequest("   ", Config(40m)));
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
    }

    // ------------------------------------------------------------------- 6

    [Fact]
    public async Task An_unusual_but_workable_table_is_allowed()
    {
        // Flat, and rising. Both are strange and neither is wrong, and a church
        // that wants them should not be argued with by a validator.
        var client = Client;

        var flat = await CreateAsync(client, "Everyone Gets 25", Config(25m, 25m, 25m, 25m));
        Assert.Equal(4, flat.Config.PlacePoints.Count);

        var rising = await CreateAsync(client, "Last Is Best", Config(10m, 20m, 30m, 40m));
        Assert.Equal(10m, rising.Config.PlacePoints[0]);
    }

    // ------------------------------------------------------------------- 7

    [Fact]
    public async Task The_default_cannot_be_retired_or_deleted_and_moving_it_leaves_exactly_one()
    {
        var client = Client;
        var made = await CreateAsync(client, "Second Set", Config(50m, 30m, 20m, 10m));

        var profiles = await client.GetFromJsonAsync<List<ScoringProfileDto>>(Profiles);
        var current = profiles!.Single(p => p.IsDefault);

        var retire = await client.PostAsync($"{Profiles}/{current.Id}/retire", null);
        Assert.Equal(HttpStatusCode.Conflict, retire.StatusCode);

        var delete = await client.DeleteAsync($"{Profiles}/{current.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);

        // Move the default, and exactly one row still carries it.
        var moved = await client.PostAsync($"{Profiles}/{made.Id}/default", null);
        moved.EnsureSuccessStatusCode();

        var after = (await moved.Content.ReadFromJsonAsync<List<ScoringProfileDto>>())!;
        Assert.Equal(1, after.Count(p => p.IsDefault));
        Assert.True(after.Single(p => p.Id == made.Id).IsDefault);

        // Put it back, so the rest of the collection sees what it expects.
        (await client.PostAsync($"{Profiles}/{current.Id}/default", null)).EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------------- 8

    [Fact]
    public async Task Rules_a_session_has_used_cannot_be_deleted_and_say_to_retire_them()
    {
        var client = Client;
        var (divisionId, _, _) = await fixture.FixtureIdsAsync();

        var profile = await CreateAsync(client, "Used Once", Config(40m, 30m, 20m, 10m));

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 2, 5), profile.Id));
        created.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"{Profiles}/{profile.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("scoring_profile_in_use", problem!["code"].ToString());
        Assert.Contains("Retire them instead", problem["detail"].ToString());

        // Retiring works, and takes it out of what a new session can choose.
        (await client.PostAsync($"{Profiles}/{profile.Id}/retire", null)).EnsureSuccessStatusCode();

        var refused = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 2, 12), profile.Id));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ------------------------------------------------------------------- 9

    [Fact]
    public async Task Unused_rules_can_be_deleted()
    {
        var client = Client;
        var profile = await CreateAsync(client, "Added By Mistake", Config(40m, 30m));

        var response = await client.DeleteAsync($"{Profiles}/{profile.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ------------------------------------------------------------------ 10

    [Fact]
    public async Task The_preview_works_the_rules_through_on_real_rounds()
    {
        // What makes the editor teachable: tie and DQ rules cannot be predicted
        // from their names, so the screen shows what they do.
        var client = Client;

        var response = await client.PostAsJsonAsync(
            $"{Profiles}/preview", Config(40m, 30m, 20m, 10m));
        response.EnsureSuccessStatusCode();

        var preview = (await response.Content.ReadFromJsonAsync<ScoringPreviewDto>())!;
        Assert.Equal(4, preview.Examples.Count);

        var clean = preview.Examples[0];
        Assert.Equal(40m, clean.Teams.Single(t => t.Place == 1).Points);
        Assert.Null(clean.Rejected);

        // The averaging rule, which is the one people argue about: two teams
        // tying for first consume slots 1 and 2, so they share 40 and 30 for 35
        // each. The next team is therefore at SLOT 3 and takes 20. There is no
        // slot 2 left to be at, which is the part that surprises people.
        var tie = preview.Examples[1];
        var tied = tie.Teams.Where(t => t.Place == 1).ToList();
        Assert.Equal(2, tied.Count);
        Assert.All(tied, t => Assert.Equal(35m, t.Points));
        Assert.DoesNotContain(tie.Teams, t => t.Place == 2);
        Assert.Equal(20m, tie.Teams.Single(t => t.Place == 3).Points);

        // All four level share the whole table: 100 over four is 25 each.
        Assert.All(preview.Examples[2].Teams, t => Assert.Equal(25m, t.Points));

        // The default DQ rule holds the slot, so second place does not inherit
        // first: it still earns 30.
        var dq = preview.Examples[3];
        Assert.Equal(0m, dq.Teams.Single(t => t.IsDisqualified).Points);
        Assert.Equal(30m, dq.Teams.Single(t => t.Place == 2).Points);
    }

    // ------------------------------------------------------------------ 11

    [Fact]
    public async Task The_preview_shows_a_rule_refusing_a_round_rather_than_failing()
    {
        // "Ties are not allowed" has no scored outcome to show, and saying so
        // on the tie examples is the clearest demonstration of that setting.
        var client = Client;

        var response = await client.PostAsJsonAsync(
            $"{Profiles}/preview",
            new ScoringConfig { PlacePoints = [40m, 30m, 20m, 10m], TieRule = TieRule.NoTiesAllowed });

        response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<ScoringPreviewDto>())!;

        Assert.Null(preview.Examples[0].Rejected);
        Assert.NotNull(preview.Examples[1].Rejected);
        Assert.NotNull(preview.Examples[2].Rejected);
    }

    // ------------------------------------------------------------------ 12

    [Fact]
    public async Task The_preview_refuses_rules_that_cannot_be_used_at_all()
    {
        var response = await Client.PostAsJsonAsync($"{Profiles}/preview", Config());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("invalid_scoring_config", problem!["code"].ToString());
    }

    // ------------------------------------------------------------------ 13

    [Fact]
    public async Task A_games_leader_can_read_and_preview_but_only_an_admin_can_change_the_rules()
    {
        // Choosing which rules a session runs under happens on the new session
        // form, so a games leader has to be able to see the list. Deciding what
        // the rules ARE is a bigger call.
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.RoleHeader, nameof(UserRole.GamesLeader));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Profiles)).StatusCode);

        var preview = await client.PostAsJsonAsync($"{Profiles}/preview", Config(40m, 30m));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        var create = await client.PostAsJsonAsync(
            Profiles, new SaveScoringProfileRequest("Not Allowed", Config(40m)));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    // ------------------------------------------------------------------ 14

    [Fact]
    public async Task A_scorekeeper_can_read_the_rules_but_a_viewer_cannot()
    {
        // The scorekeeper is the one asked why a tie scored the way it did, so
        // they can open the rules and look, and preview what a table does. They
        // still cannot change any of it.
        var scorekeeper = fixture.CreateClient();
        scorekeeper.DefaultRequestHeaders.Add(TestAuth.RoleHeader, nameof(UserRole.Scorekeeper));

        Assert.Equal(HttpStatusCode.OK, (await scorekeeper.GetAsync(Profiles)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scorekeeper.PostAsJsonAsync($"{Profiles}/preview", Config(40m, 30m))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await scorekeeper.PostAsJsonAsync(Profiles, new SaveScoringProfileRequest("Not Allowed", Config(40m)))).StatusCode);

        var viewer = fixture.CreateClient();
        viewer.DefaultRequestHeaders.Add(TestAuth.RoleHeader, nameof(UserRole.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Profiles)).StatusCode);
    }

    // ------------------------------------------------------------------ 20

    [Fact]
    public async Task A_session_in_setup_can_have_its_scoring_changed_and_a_started_one_cannot()
    {
        // A night made a week in advance has decided nothing yet. Once it has
        // started, the rules are frozen and moving the pointer would leave it
        // naming one set and scoring by another.
        var client = Client;
        var (divisionId, _, _) = await fixture.FixtureIdsAsync();

        var alternative = await CreateAsync(client, "Changed To", Config(70m, 50m, 30m, 10m));

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 4, 2)));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;

        Assert.Equal("Official AWANA", session.Scoring.Name);

        var changed = await client.PutAsJsonAsync(
            $"/api/sessions/{session.Id}/scoring", new SetSessionScoringRequest(alternative.Id));
        changed.EnsureSuccessStatusCode();

        var afterChange = (await changed.Content.ReadFromJsonAsync<SessionDetailDto>())!;
        Assert.Equal("Changed To", afterChange.Scoring.Name);
        Assert.False(afterChange.Scoring.IsDefault);

        // Null puts it back on the default.
        var cleared = await client.PutAsJsonAsync(
            $"/api/sessions/{session.Id}/scoring", new SetSessionScoringRequest(null));
        cleared.EnsureSuccessStatusCode();
        Assert.True((await cleared.Content.ReadFromJsonAsync<SessionDetailDto>())!.Scoring.IsDefault);

        // Set it again, then start, and the choice is what gets frozen.
        (await client.PutAsJsonAsync(
            $"/api/sessions/{session.Id}/scoring", new SetSessionScoringRequest(alternative.Id)))
            .EnsureSuccessStatusCode();

        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        var started = await client.GetFromJsonAsync<SessionDetailDto>($"/api/sessions/{session.Id}");
        Assert.Equal<decimal[]>([70m, 50m, 30m, 10m], [.. started!.Scoring.PlacePoints]);

        // And now it is refused.
        var late = await client.PutAsJsonAsync(
            $"/api/sessions/{session.Id}/scoring", new SetSessionScoringRequest(null));

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);

        var problem = await late.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("session_already_started", problem!["code"].ToString());
    }

    // ------------------------------------------------------------------ 18

    [Fact]
    public async Task A_session_says_which_rules_it_is_on_before_and_after_starting()
    {
        // Shown on the console even when the church has only one set and there
        // was never a choice to make.
        var client = Client;
        var (divisionId, _, _) = await fixture.FixtureIdsAsync();

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 3, 5)));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;

        // In setup it reports what it WOULD use, and says so.
        Assert.Equal("Official AWANA", session.Scoring.Name);
        Assert.True(session.Scoring.IsDefault);
        Assert.False(session.Scoring.IsFixed);
        Assert.Equal<decimal[]>([40m, 30m, 20m, 10m], [.. session.Scoring.PlacePoints]);

        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        var started = await client.GetFromJsonAsync<SessionDetailDto>($"/api/sessions/{session.Id}");

        // Started, the rules are frozen and it no longer reads as the default.
        Assert.True(started!.Scoring.IsFixed);
        Assert.False(started.Scoring.IsDefault);
        Assert.Equal("Official AWANA", started.Scoring.Name);
    }

    // ------------------------------------------------------------------ 19

    [Fact]
    public async Task A_started_session_reports_its_frozen_numbers_not_the_edited_profile()
    {
        var client = Client;
        var (divisionId, _, _) = await fixture.FixtureIdsAsync();

        var profile = await CreateAsync(client, "Reported Frozen", Config(40m, 30m, 20m, 10m));

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2027, 3, 12), profile.Id));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;

        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(
            $"{Profiles}/{profile.Id}",
            new SaveScoringProfileRequest("Reported Frozen", Config(99m, 88m))))
            .EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<SessionDetailDto>($"/api/sessions/{session.Id}");

        // The frozen copy, not what the profile says now.
        Assert.Equal<decimal[]>([40m, 30m, 20m, 10m], [.. after!.Scoring.PlacePoints]);
    }

    // ------------------------------------------------------------------ 16

    [Fact]
    public async Task The_standard_table_is_read_only_even_to_an_admin()
    {
        // The name is a claim. A set called Official AWANA that has been edited
        // into something else is a lie to whoever reads it next.
        var client = Client;

        var profiles = await client.GetFromJsonAsync<List<ScoringProfileDto>>(Profiles);
        var official = profiles!.Single(p => p.Name == "Official AWANA");

        Assert.True(official.IsSeeded);

        var edit = await client.PutAsJsonAsync(
            $"{Profiles}/{official.Id}",
            new SaveScoringProfileRequest("Official AWANA", Config(50m, 25m, 15m, 10m)));

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        var problem = await edit.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("scoring_profile_is_seeded", problem!["code"].ToString());
        Assert.Contains("Duplicate it", problem["detail"].ToString());
    }

    // ------------------------------------------------------------------ 17

    [Fact]
    public async Task Duplicating_gives_an_editable_copy_with_a_free_name()
    {
        // How anything gets changed about the standard table, and the usual way
        // a second set is made at all.
        var client = Client;

        var profiles = await client.GetFromJsonAsync<List<ScoringProfileDto>>(Profiles);
        var official = profiles!.Single(p => p.Name == "Official AWANA");

        var response = await client.PostAsync($"{Profiles}/{official.Id}/duplicate", null);
        response.EnsureSuccessStatusCode();

        var copy = (await response.Content.ReadFromJsonAsync<ScoringProfileDto>())!;

        Assert.False(copy.IsSeeded);
        Assert.False(copy.IsDefault);
        Assert.NotEqual(official.Id, copy.Id);
        Assert.Equal<decimal[]>([.. official.Config.PlacePoints], [.. copy.Config.PlacePoints]);

        // And the copy edits, which the original would not.
        var edit = await client.PutAsJsonAsync(
            $"{Profiles}/{copy.Id}", new SaveScoringProfileRequest(copy.Name, Config(50m, 25m)));
        edit.EnsureSuccessStatusCode();

        // A second copy does not collide on the name.
        var again = await client.PostAsync($"{Profiles}/{official.Id}/duplicate", null);
        again.EnsureSuccessStatusCode();
        var second = (await again.Content.ReadFromJsonAsync<ScoringProfileDto>())!;

        Assert.NotEqual(copy.Name, second.Name);
    }

    // ------------------------------------------------------------------ 15

    [Fact]
    public async Task Two_sets_of_rules_cannot_share_a_name()
    {
        var client = Client;
        await CreateAsync(client, "Tournament Night", Config(50m, 30m, 20m, 10m));

        var response = await client.PostAsJsonAsync(
            Profiles, new SaveScoringProfileRequest("tournament night", Config(40m)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("scoring_profile_name_taken", problem!["code"].ToString());
    }
}
