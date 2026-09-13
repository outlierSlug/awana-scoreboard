using System.Security.Claims;
using System.Text.Encodings.Web;
using Awana.Api.Auth;
using Awana.Data;
using Awana.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Awana.Api.Tests;

/// <summary>
/// Boots the real application against a throwaway Postgres in Docker.
///
/// A real database rather than an in-memory provider, because the behavior
/// being tested here IS database behavior: a filtered unique index, a
/// transaction, and cascade rules. An in-memory provider would pass these tests
/// while the real thing failed.
/// </summary>
public class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Pinned to the same major version production runs.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("awana_test")
        .WithUsername("awana")
        .WithPassword("test")
        .Build();

    // Explicit implementation, because WebApplicationFactory already exposes a
    // ValueTask DisposeAsync and xUnit's lifetime interface wants Task.
    Task IAsyncLifetime.InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Seed:ChurchSlug", "test-church");
        builder.UseSetting("Seed:ChurchName", "Test Church");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5200");

        // Somebody has to be on the allowlist or nobody can sign in, here or
        // anywhere else.
        builder.UseSetting("Seed:AdminEmails", TestAuth.AdminEmail);

        // Google is not reachable from a test, so the tests carry their own
        // scheme and authenticate as a seeded user. The endpoints, policies and
        // claim reading under test are the real ones: only the step that proves
        // who you are is swapped out.
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuth.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuth.Scheme, _ => { });

            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuth.Scheme;
                options.DefaultAuthenticateScheme = TestAuth.Scheme;
                options.DefaultChallengeScheme = TestAuth.Scheme;
            });
        });
    }

    /// <summary>A scope for reaching into the database from a test.</summary>
    public async Task<T> WithDbAsync<T>(Func<AwanaDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AwanaDbContext>();
        return await work(db);
    }

    /// <summary>The seeded T&amp;T division and its four teams.</summary>
    public Task<(Guid DivisionId, Guid GameId, List<Guid> TeamIds)> FixtureIdsAsync() =>
        WithDbAsync(async db =>
        {
            var division = await db.Divisions.FirstAsync(d => d.Slug == "tnt");

            var teams = await db.Teams
                .Where(t => t.DivisionId == division.Id)
                .OrderBy(t => t.SortOrder)
                .Select(t => t.Id)
                .ToListAsync();

            var game = await db.Games.FirstAsync(g => g.Name == "Baton Relay");

            return (division.Id, game.Id, teams);
        });
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<ApiFixture>;

public static class TestAuth
{
    public const string Scheme = "Test";
    public const string AdminEmail = "test-admin@example.com";

    /// <summary>Send this to act as a lesser role, for checking a policy.</summary>
    public const string RoleHeader = "X-Test-Role";

    /// <summary>Send this as the role to act as nobody at all.</summary>
    public const string Anonymous = "none";
}

/// <summary>
/// Authenticates every request as the seeded admin, or as whatever role the
/// request asks for.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AwanaDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var asked = Request.Headers[TestAuth.RoleHeader].ToString();

        // NoResult rather than Fail: an unauthenticated request is the ordinary
        // case for a signed-out visitor, and it has to reach the endpoint's own
        // authorization rather than be rejected by the scheme.
        if (asked == TestAuth.Anonymous) return AuthenticateResult.NoResult();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == TestAuth.AdminEmail);
        if (user is null) return AuthenticateResult.Fail("The admin was not seeded.");

        if (Enum.TryParse<UserRole>(asked, out var role))
        {
            // Not saved: this only changes who the request claims to be.
            user = new AppUser
            {
                Id = user.Id,
                ChurchId = user.ChurchId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = role,
            };
        }

        var principal = AuthClaims.ToPrincipal(user, TestAuth.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, TestAuth.Scheme));
    }
}
