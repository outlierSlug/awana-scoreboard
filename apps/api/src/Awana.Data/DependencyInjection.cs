using Awana.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Awana.Data;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database. Kept here rather than in the API so the EF and
    /// Npgsql packages stay behind this boundary.
    /// </summary>
    public static IServiceCollection AddAwanaData(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. The API cannot start without a database.");

        services.AddDbContext<AwanaDbContext>(options => options
            .UseNpgsql(connectionString)
            // Idiomatic Postgres identifiers, so anyone can open psql during a
            // game night and query without quoting every column name.
            .UseSnakeCaseNamingConvention());

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddScoped<DbSeeder>();
        services.AddHostedService<DatabaseInitializer>();

        return services;
    }
}

public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Lets migrations be switched off from the hosting dashboard without a
    /// redeploy, which is what you want when a migration is the thing going
    /// wrong at a bad moment.
    /// </summary>
    public bool RunMigrationsOnStartup { get; set; } = true;

    public bool RunSeedOnStartup { get; set; } = true;

    /// <summary>
    /// Direct connection for DDL. Neon fronts Postgres with PgBouncer in
    /// transaction mode, and migrations must not run through a pooler.
    /// Falls back to the ordinary connection when unset, which is correct
    /// locally where there is no pooler.
    /// </summary>
    public string? MigrationsConnectionString { get; set; }
}
