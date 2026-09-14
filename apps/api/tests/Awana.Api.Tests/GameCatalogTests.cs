using System.Net;
using System.Net.Http.Json;
using Awana.Api.Contracts;
using Awana.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Tests;

/// <summary>
/// The catalog is the first screen that edits reference data rather than
/// tonight's scores, so the things worth pinning down are the ones that protect
/// history: a played game cannot be deleted, a rename does not orphan the
/// rounds that used it, and retiring takes a game out of the picker while
/// leaving the past alone.
/// </summary>
[Collection(nameof(ApiCollection))]
public class GameCatalogTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.CreateClient();

    private const string Catalog = "/api/catalog/games";

    private static SaveGameRequest NewGame(string name, string? notes = null) =>
        new(name, notes);

    private async Task<GameDetailDto> CreateAsync(HttpClient client, SaveGameRequest request)
    {
        var response = await client.PostAsJsonAsync(Catalog, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    /// <summary>Plays one round of a game, so it stops being deletable.</summary>
    private async Task PlayAsync(HttpClient client, Guid gameId, DateOnly date)
    {
        var (divisionId, _, teams) = await fixture.FixtureIdsAsync();

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, date));
        created.EnsureSuccessStatusCode();

        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;
        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        var round = new CreateRoundRequest(
            ClientRequestId: Guid.NewGuid(),
            GameId: gameId,
            Multiplier: 1m,
            Entries: teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList());

        (await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", round))
            .EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------------- 1

    [Fact]
    public async Task The_seeded_catalog_is_there_and_carries_its_seed_keys()
    {
        // The keys are what let the seeder recognize its own rows on the next
        // boot. Without them a renamed game gets a duplicate created beside it.
        var games = await Client.GetFromJsonAsync<List<GameDetailDto>>(Catalog);

        Assert.NotNull(games);

        // Every seeded game, by name, is present and marked as seeded. Asserted
        // over this list rather than the whole catalog, because the other tests
        // in this collection share the database and add games of their own.
        foreach (var name in new[] { "Baton Relay", "Tug-of-War", "Beanbag Curling", "Steal" })
        {
            var game = Assert.Single(games, g => g.Name == name);
            Assert.True(game.IsSeeded, $"{name} should carry a seed key.");
        }

        Assert.Contains(games, g => g.Name == "Baton Relay" && g.Notes is not null);

        // One hand-arranged list, seeded with the four iconic games first.
        var seeded = games.Where(g => g.IsSeeded).ToList();
        Assert.Equal("Baton Relay", seeded[0].Name);
        Assert.Equal("Tug-of-War", seeded[3].Name);
    }

    // ------------------------------------------------------------------- 2

    [Fact]
    public async Task A_game_that_has_been_played_cannot_be_deleted_and_says_to_retire_it()
    {
        // The whole reason retiring exists. Deleting would break every round
        // that names the game, and the board has to keep reading correctly.
        var client = Client;
        var game = await CreateAsync(client, NewGame("Bucket Brigade"));

        await PlayAsync(client, game.Id, new DateOnly(2026, 11, 6));

        var response = await client.DeleteAsync($"{Catalog}/{game.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("game_in_use", problem!["code"].ToString());
        Assert.Contains("Retire it instead", problem["detail"].ToString());
    }

    // ------------------------------------------------------------------- 3

    [Fact]
    public async Task A_game_nobody_played_can_be_deleted()
    {
        var client = Client;
        var game = await CreateAsync(client, NewGame("Added By Mistake"));

        var response = await client.DeleteAsync($"{Catalog}/{game.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var games = await client.GetFromJsonAsync<List<GameDetailDto>>(Catalog);
        Assert.DoesNotContain(games!, g => g.Id == game.Id);
    }

    // ------------------------------------------------------------------- 4

    [Fact]
    public async Task Retiring_hides_a_game_from_the_picker_and_leaves_its_rounds_alone()
    {
        var client = Client;
        var game = await CreateAsync(client, NewGame("Sock Toss"));

        await PlayAsync(client, game.Id, new DateOnly(2026, 11, 13));

        (await client.PostAsync($"{Catalog}/{game.Id}/retire", null)).EnsureSuccessStatusCode();

        // Gone from what a scorekeeper can pick.
        var picker = await client.GetFromJsonAsync<List<GameDto>>("/api/games");
        Assert.DoesNotContain(picker!, g => g.Id == game.Id);

        // Still in the catalog, marked retired, with its round still counted.
        var catalog = await client.GetFromJsonAsync<List<GameDetailDto>>(Catalog);
        var retired = Assert.Single(catalog!, g => g.Id == game.Id);
        Assert.False(retired.IsActive);
        Assert.Equal(1, retired.RoundCount);

        // And the round itself is untouched.
        var stillThere = await fixture.WithDbAsync(db =>
            db.Rounds.AnyAsync(r => r.GameId == game.Id && r.VoidedAt == null));
        Assert.True(stillThere);

        // Restoring puts it back.
        (await client.PostAsync($"{Catalog}/{game.Id}/restore", null)).EnsureSuccessStatusCode();
        var again = await client.GetFromJsonAsync<List<GameDto>>("/api/games");
        Assert.Contains(again!, g => g.Id == game.Id);
    }

    // ------------------------------------------------------------------- 5

    [Fact]
    public async Task Renaming_a_game_renames_it_everywhere_it_was_played()
    {
        // Deliberate: a rename is correcting what the game has always been
        // called, not asserting that a different game was played last March.
        var client = Client;
        var game = await CreateAsync(client, NewGame("Stel"));

        await PlayAsync(client, game.Id, new DateOnly(2026, 11, 20));

        var response = await client.PutAsJsonAsync(
            $"{Catalog}/{game.Id}", NewGame("Steal the Bacon", "Two teams, one beanbag."));
        response.EnsureSuccessStatusCode();

        var updated = (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
        Assert.Equal("Steal the Bacon", updated.Name);
        Assert.Equal("Two teams, one beanbag.", updated.Notes);
        Assert.Equal(1, updated.RoundCount);

        var name = await fixture.WithDbAsync(db => db.Rounds
            .Where(r => r.GameId == game.Id)
            .Select(r => r.Game.Name)
            .FirstAsync());

        Assert.Equal("Steal the Bacon", name);
    }

    // ------------------------------------------------------------------- 6

    [Fact]
    public async Task Two_games_cannot_share_a_name()
    {
        // The unique index would refuse this anyway. The point is that the
        // caller gets a sentence about a duplicate rather than a 500.
        var client = Client;
        await CreateAsync(client, NewGame("Hoop Roll"));

        var response = await client.PostAsJsonAsync(Catalog, NewGame("hoop roll"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Contains("game_name_taken", problem!["code"].ToString());
    }

    // ------------------------------------------------------------------- 7

    [Fact]
    public async Task A_blank_name_and_an_overlong_note_are_both_rejected()
    {
        var client = Client;

        var blank = await client.PostAsJsonAsync(Catalog, NewGame("   "));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var wordy = await client.PostAsJsonAsync(
            Catalog, NewGame("Essay Relay", new string('x', 2001)));
        Assert.Equal(HttpStatusCode.BadRequest, wordy.StatusCode);
    }

    // ------------------------------------------------------------------ 10

    [Fact]
    public async Task Reordering_renumbers_the_games_it_names_and_ignores_a_stale_id()
    {
        // A stale id is the ordinary case rather than an error: somebody else
        // deleted a game while this page was open, and rejecting the whole
        // reorder over it would lose a drag that was otherwise fine.
        var client = Client;

        var a = await CreateAsync(client, NewGame("Order A"));
        var b = await CreateAsync(client, NewGame("Order B"));

        var response = await client.PutAsJsonAsync(
            $"{Catalog}/order", new ReorderGamesRequest([b.Id, a.Id, Guid.NewGuid()]));
        response.EnsureSuccessStatusCode();

        var games = (await response.Content.ReadFromJsonAsync<List<GameDetailDto>>())!;
        var orderOfB = games.Single(g => g.Id == b.Id).SortOrder;
        var orderOfA = games.Single(g => g.Id == a.Id).SortOrder;

        Assert.True(orderOfB < orderOfA);
    }

    // ------------------------------------------------------------------ 11

    [Fact]
    public async Task A_scorekeeper_can_read_the_picker_but_cannot_edit_the_catalog()
    {
        // Roles stop at what the job needs: a scorekeeper records what was
        // played, a games leader decides what is on the list.
        var client = Client;
        client.DefaultRequestHeaders.Add(TestAuth.RoleHeader, nameof(UserRole.Scorekeeper));

        var picker = await client.GetAsync("/api/games");
        Assert.Equal(HttpStatusCode.OK, picker.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Catalog)).StatusCode);

        var create = await client.PostAsJsonAsync(Catalog, NewGame("Not Allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    // ------------------------------------------------------------------ 12

    [Fact]
    public async Task A_games_leader_can_edit_the_catalog_but_only_an_admin_can_delete()
    {
        var client = Client;
        var game = await CreateAsync(client, NewGame("Leader Made This"));

        var asLeader = fixture.CreateClient();
        asLeader.DefaultRequestHeaders.Add(TestAuth.RoleHeader, nameof(UserRole.GamesLeader));

        var edit = await asLeader.PutAsJsonAsync($"{Catalog}/{game.Id}", NewGame("Leader Renamed It"));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var retire = await asLeader.PostAsync($"{Catalog}/{game.Id}/retire", null);
        Assert.Equal(HttpStatusCode.OK, retire.StatusCode);

        var delete = await asLeader.DeleteAsync($"{Catalog}/{game.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    // ------------------------------------------------------------------ 13

    [Fact]
    public async Task A_signed_out_visitor_gets_nothing_from_the_catalog()
    {
        var client = Client;
        client.DefaultRequestHeaders.Add(TestAuth.RoleHeader, TestAuth.Anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Catalog)).StatusCode);
    }

    // ------------------------------------------------------------------ 14

    [Fact]
    public async Task Editing_the_catalog_is_written_to_the_audit_log()
    {
        // Reference data is exactly the kind of thing somebody changes and
        // nobody remembers changing.
        var client = Client;
        var game = await CreateAsync(client, NewGame("Audited Relay"));

        (await client.PostAsync($"{Catalog}/{game.Id}/retire", null)).EnsureSuccessStatusCode();

        var actions = await fixture.WithDbAsync(db => db.AuditLogs
            .Where(a => a.EntityId == game.Id)
            .Select(a => a.Action)
            .ToListAsync());

        Assert.Contains("game.created", actions);
        Assert.Contains("game.retired", actions);
    }
}
