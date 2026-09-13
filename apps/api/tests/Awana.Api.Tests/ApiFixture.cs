using Awana.Data;
using Awana.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
