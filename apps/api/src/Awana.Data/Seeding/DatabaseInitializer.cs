using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Awana.Data.Seeding;

/// <summary>
/// Applies migrations and seeds reference data at startup.
///
/// Both steps are guarded by configuration so either can be switched off from
/// the hosting dashboard without a redeploy, and both log loudly, because a
/// silent failure here means the app comes up serving an empty or half-migrated
/// database.
///
/// Concurrent boots are safe: EF Core takes an exclusive migration lock itself,
/// so two instances starting together cannot both apply the same migration.
/// </summary>
public class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<SeedOptions> seedOptions,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = databaseOptions.Value;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AwanaDbContext>();

        if (options.RunMigrationsOnStartup)
        {
            await MigrateAsync(db, options, cancellationToken);
        }
        else
        {
            logger.LogWarning(
                "Skipping migrations: Database:RunMigrationsOnStartup is false. " +
                "The schema must already be up to date.");
        }

        if (options.RunSeedOnStartup)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
            await seeder.SeedAsync(seedOptions.Value, cancellationToken);
            logger.LogInformation("Seeding complete.");
        }
        else
        {
            logger.LogWarning("Skipping seed: Database:RunSeedOnStartup is false.");
        }
    }

    private async Task MigrateAsync(
        AwanaDbContext db, DatabaseOptions options, CancellationToken ct)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        logger.LogInformation(
            "Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));

        if (options.MigrationsConnectionString is { Length: > 0 } direct)
        {
            // A separate context on the direct connection, because DDL must not
            // go through a transaction-mode pooler.
            var builder = new DbContextOptionsBuilder<AwanaDbContext>()
                .UseNpgsql(direct)
                .UseSnakeCaseNamingConvention();

            await using var migrationDb = new AwanaDbContext(builder.Options);
            await migrationDb.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.MigrateAsync(ct);
        }

        logger.LogInformation("Migrations applied.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
