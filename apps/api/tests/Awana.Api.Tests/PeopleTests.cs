using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Awana.Api.Auth;
using Awana.Api.Contracts;
using Awana.Api.Services;
using Awana.Data;
using Awana.Data.Entities;
using Awana.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Awana.Api.Tests;

/// <summary>
/// Managing access is the one feature where a bug locks people out or lets
/// them keep something they were meant to lose, so what is pinned down here is
/// mostly refusal: nobody edits their own access, the last admin cannot be
/// removed, configuration admins cannot be undone behind a restart's back, and
/// taking access away takes effect without waiting for a sign-out.
/// </summary>
[Collection(nameof(ApiCollection))]
public class PeopleTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.CreateClient();

    private static string NewEmail() => $"leader-{Guid.NewGuid():N}@example.com";

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<PersonDto> AddAsync(HttpClient client, string email, string role)
    {
        var response = await client.PostAsJsonAsync("/api/people", new AddPersonRequest(email, role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PersonDto>())!;
    }

    private Task<Guid> TestAdminIdAsync() =>
        fixture.WithDbAsync(db => db.Users.Where(u => u.Email == TestAuth.AdminEmail).Select(u => u.Id).FirstAsync());

    // ------------------------------------------------------------------- 1

    [Fact]
    public async Task An_admin_adds_a_leader_who_then_appears_with_that_role()
    {
        var client = Client;
        var email = NewEmail();

        // Typed the way people type addresses into a form.
        var added = await AddAsync(client, $"  {email.ToUpperInvariant()} ", nameof(UserRole.Scorekeeper));

        Assert.Equal(email, added.Email);
        Assert.Equal(nameof(UserRole.Scorekeeper), added.Role);
        Assert.False(added.HasSignedIn);

        var everyone = await client.GetFromJsonAsync<List<PersonDto>>("/api/people");
        Assert.Contains(everyone!, p => p.Id == added.Id && p.Role == nameof(UserRole.Scorekeeper));

        // The same address twice is somebody who already has an account.
        var again = await client.PostAsJsonAsync("/api/people", new AddPersonRequest(email, nameof(UserRole.Admin)));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("person_exists", await CodeOf(again));
    }

    [Fact]
    public async Task Nonsense_is_refused_rather_than_turned_into_an_account()
    {
        var client = Client;

        var notAnAddress = await client.PostAsJsonAsync(
            "/api/people", new AddPersonRequest("not an address", nameof(UserRole.Scorekeeper)));
        Assert.Equal(HttpStatusCode.BadRequest, notAnAddress.StatusCode);

        // Enum.TryParse would have taken this as Admin.
        var numericRole = await client.PostAsJsonAsync("/api/people", new AddPersonRequest(NewEmail(), "40"));
        Assert.Equal(HttpStatusCode.BadRequest, numericRole.StatusCode);
    }

    // ------------------------------------------------------------------- 2

    [Fact]
    public async Task A_role_change_lands_in_the_activity_log_with_who_made_it()
    {
        var client = Client;
        var person = await AddAsync(client, NewEmail(), nameof(UserRole.Scorekeeper));

        var changed = await client.PutAsJsonAsync(
            $"/api/people/{person.Id}/role", new SetRoleRequest(nameof(UserRole.GamesLeader)));
        changed.EnsureSuccessStatusCode();

        var page = await client.GetFromJsonAsync<ActivityPageDto>("/api/activity?limit=20");

        var entry = page!.Entries.First(e => e.Action == "person.role_changed" && e.EntityId == person.Id);

        Assert.Equal(TestAuth.AdminEmail, entry.ActorName);
        Assert.Equal(nameof(UserRole.Scorekeeper), entry.Data!.Value.GetProperty("From").GetString());
        Assert.Equal(nameof(UserRole.GamesLeader), entry.Data!.Value.GetProperty("To").GetString());
    }

    // ------------------------------------------------------------------- 3

    [Fact]
    public async Task Nobody_can_change_or_remove_their_own_access()
    {
        var client = Client;
        var me = await TestAdminIdAsync();

        var demote = await client.PutAsJsonAsync(
            $"/api/people/{me}/role", new SetRoleRequest(nameof(UserRole.Viewer)));
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("person_is_you", await CodeOf(demote));

        var deactivate = await client.PostAsync($"/api/people/{me}/deactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal("person_is_you", await CodeOf(deactivate));
    }

    // ------------------------------------------------------------------- 4

    [Fact]
    public async Task A_configured_admin_cannot_be_changed_from_the_page()
    {
        // Acting as a second admin, since the test admin is both the caller and
        // the configured admin, and the self check would answer first.
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PeopleService>();
        var db = scope.ServiceProvider.GetRequiredService<AwanaDbContext>();

        var church = await db.Churches.Where(c => c.Slug == "test-church").Select(c => c.Id).FirstAsync();
        var configAdmin = await TestAdminIdAsync();

        var otherAdmin = new AppUser { ChurchId = church, Email = NewEmail(), DisplayName = "Other", Role = UserRole.Admin };
        db.Users.Add(otherAdmin);
        await db.SaveChangesAsync();

        var result = await service.SetRoleAsync(
            church, configAdmin, new SetRoleRequest(nameof(UserRole.Viewer)), otherAdmin.Id);

        Assert.False(result.Ok);
        Assert.Equal("person_is_config_admin", result.Error!.Code);
    }

    // ------------------------------------------------------------------- 5

    [Fact]
    public async Task The_last_active_admin_cannot_be_demoted_or_deactivated()
    {
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PeopleService>();
        var db = scope.ServiceProvider.GetRequiredService<AwanaDbContext>();

        // A church of its own, so the seeded test admin does not count as the
        // other admin.
        var church = new Church { Name = "Lonely", Slug = $"lonely-{Guid.NewGuid():N}", TimeZoneId = "America/Los_Angeles" };
        var onlyAdmin = new AppUser { Church = church, Email = NewEmail(), DisplayName = "Only", Role = UserRole.Admin };
        var caller = new AppUser { Church = church, Email = NewEmail(), DisplayName = "Caller", Role = UserRole.Scorekeeper };
        db.AddRange(church, onlyAdmin, caller);
        await db.SaveChangesAsync();

        var demote = await service.SetRoleAsync(
            church.Id, onlyAdmin.Id, new SetRoleRequest(nameof(UserRole.Scorekeeper)), caller.Id);
        Assert.Equal("person_last_admin", demote.Error?.Code);

        var deactivate = await service.SetActiveAsync(church.Id, onlyAdmin.Id, false, caller.Id);
        Assert.Equal("person_last_admin", deactivate.Error?.Code);

        // With a second admin the same change is fine.
        var promoted = await service.SetRoleAsync(
            church.Id, caller.Id, new SetRoleRequest(nameof(UserRole.Admin)), onlyAdmin.Id);
        Assert.True(promoted.Ok);

        var nowAllowed = await service.SetRoleAsync(
            church.Id, onlyAdmin.Id, new SetRoleRequest(nameof(UserRole.Scorekeeper)), caller.Id);
        Assert.True(nowAllowed.Ok);
    }

    // ------------------------------------------------------------------- 6

    [Theory]
    [InlineData(nameof(UserRole.Viewer))]
    [InlineData(nameof(UserRole.Scorekeeper))]
    [InlineData(nameof(UserRole.GamesLeader))]
    public async Task Only_admins_can_see_or_change_people(string role)
    {
        // This is the one payload that lists other people's addresses.
        var client = Client;

        foreach (var request in new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "/api/people"),
            new HttpRequestMessage(HttpMethod.Get, "/api/activity"),
            new HttpRequestMessage(HttpMethod.Post, "/api/people")
            {
                Content = JsonContent.Create(new AddPersonRequest(NewEmail(), nameof(UserRole.Admin))),
            },
        })
        {
            using (request)
            {
                request.Headers.Add(TestAuth.RoleHeader, role);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
            }
        }
    }

    // ------------------------------------------------------------------- 7

    [Fact]
    public async Task Taking_access_away_ends_an_existing_session_without_waiting_for_sign_out()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AwanaDbContext>();

        var church = await db.Churches.Where(c => c.Slug == "test-church").Select(c => c.Id).FirstAsync();
        var user = new AppUser { ChurchId = church, Email = NewEmail(), DisplayName = "Volunteer", Role = UserRole.Scorekeeper };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // What the cookie held when they signed in.
        var session = AuthClaims.ToPrincipal(user, "Cookies");

        var (untouched, _) = await SessionRevalidation.CheckAsync(db, session, "Cookies");
        Assert.Equal(SessionRevalidation.Outcome.Unchanged, untouched);

        // Promoted while signed in: carries on, with the new role.
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.GamesLeader));

        var (refreshed, principal) = await SessionRevalidation.CheckAsync(db, session, "Cookies");
        Assert.Equal(SessionRevalidation.Outcome.Refreshed, refreshed);
        Assert.Equal(UserRole.GamesLeader, principal.RoleOf());

        // Deactivated while signed in: the session ends.
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));

        var (revoked, _) = await SessionRevalidation.CheckAsync(db, session, "Cookies");
        Assert.Equal(SessionRevalidation.Outcome.Revoked, revoked);
    }

    // ------------------------------------------------------------------- 8

    [Fact]
    public async Task A_restart_keeps_roles_set_on_the_page_but_restores_configured_admins()
    {
        var configured = fixture.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var leaderEmail = NewEmail();

        var options = new SeedOptions
        {
            ChurchName = configured.ChurchName,
            ChurchSlug = configured.ChurchSlug,
            TimeZoneId = configured.TimeZoneId,
            AdminEmails = configured.AdminEmails,
            ScorekeeperEmails = leaderEmail,
        };

        // Each boot in its own scope, the way a real restart gets a fresh
        // context. Reusing one would hand the seeder the entities it tracked
        // last time, with the values they had then, and the test would be
        // checking the change tracker instead of the seeder.
        async Task BootAsync()
        {
            using var scope = fixture.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(options);
        }

        await BootAsync();

        Assert.Equal(UserRole.Scorekeeper, await fixture.WithDbAsync(db =>
            db.Users.Where(u => u.Email == leaderEmail).Select(u => u.Role).FirstAsync()));

        try
        {
            // Promoted on the People page, and every admin locked out by accident.
            await fixture.WithDbAsync(async db =>
            {
                await db.Users.Where(u => u.Email == leaderEmail)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.GamesLeader));

                return await db.Users.Where(u => u.Email == TestAuth.AdminEmail)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Viewer).SetProperty(u => u.IsActive, false));
            });

            await BootAsync();

            var (leader, admin) = await fixture.WithDbAsync(async db => (
                await db.Users.AsNoTracking().FirstAsync(u => u.Email == leaderEmail),
                await db.Users.AsNoTracking().FirstAsync(u => u.Email == TestAuth.AdminEmail)));

            // The page's decision survives the restart.
            Assert.Equal(UserRole.GamesLeader, leader.Role);

            // The way back in works.
            Assert.Equal(UserRole.Admin, admin.Role);
            Assert.True(admin.IsActive);
        }
        finally
        {
            // Every other test signs in as this admin, so it is put back even if
            // the seeder did not do it.
            await fixture.WithDbAsync(db => db.Users.Where(u => u.Email == TestAuth.AdminEmail)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Admin).SetProperty(u => u.IsActive, true)));
        }
    }

    // ------------------------------------------------------------------- 9

    [Fact]
    public async Task Activity_says_which_game_and_night_a_round_belongs_to_and_pages_back()
    {
        var client = Client;
        var (divisionId, gameId, teams) = await fixture.FixtureIdsAsync();

        var created = await client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest(divisionId, new DateOnly(2029, 3, 2)));
        created.EnsureSuccessStatusCode();
        var session = (await created.Content.ReadFromJsonAsync<SessionDetailDto>())!;

        (await client.PostAsync($"/api/sessions/{session.Id}/start", null)).EnsureSuccessStatusCode();

        var round = new CreateRoundRequest(
            Guid.NewGuid(), gameId, 1m,
            teams.Select((id, i) => new RoundEntryRequest(id, i + 1, false, 0m, null)).ToList());
        (await client.PostAsJsonAsync($"/api/sessions/{session.Id}/rounds", round)).EnsureSuccessStatusCode();

        var page = await client.GetFromJsonAsync<ActivityPageDto>("/api/activity?limit=5");
        var recorded = page!.Entries.First(e => e.Action == "round.recorded");

        // The night it happened on, rather than a round id.
        Assert.Equal(session.Id, recorded.Session?.Id);
        Assert.Equal("T&T", recorded.Session?.DivisionName);

        // And the game by name, rather than a GameId.
        Assert.Equal("Baton Relay", page.Names[gameId.ToString()]);

        // Paging continues from where the first page stopped, without repeats.
        var first = await client.GetFromJsonAsync<ActivityPageDto>("/api/activity?limit=1");
        Assert.NotNull(first!.NextBefore);

        var before = Uri.EscapeDataString(first.NextBefore!.Value.ToString("O"));
        var second = await client.GetFromJsonAsync<ActivityPageDto>($"/api/activity?limit=1&before={before}");

        Assert.Single(second!.Entries);
        Assert.NotEqual(first.Entries[0].Id, second.Entries[0].Id);
        Assert.True(second.Entries[0].At <= first.Entries[0].At);
    }
}
