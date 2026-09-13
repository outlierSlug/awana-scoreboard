using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Awana.Data;

/// <summary>
/// Used only by the EF command line tools, so that `dotnet ef migrations add`
/// does not have to boot the API and its configuration to learn the schema.
///
/// The fallback connection string points at the local Docker Postgres from
/// docker-compose.yml. It is a local-only development credential and grants
/// nothing anywhere else. Generating a migration never connects, so this is
/// mostly here to satisfy the tooling.
/// </summary>
public class AwanaDbContextFactory : IDesignTimeDbContextFactory<AwanaDbContext>
{
    private const string LocalDevelopment =
        "Host=localhost;Port=5433;Database=awana;Username=awana;Password=localdev";

    public AwanaDbContext CreateDbContext(string[] args)
    {
        // Migrations run against the DIRECT connection, never the pooler.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Migrations")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? LocalDevelopment;

        var options = new DbContextOptionsBuilder<AwanaDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AwanaDbContext(options);
    }
}
