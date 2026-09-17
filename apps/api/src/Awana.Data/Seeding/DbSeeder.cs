using Awana.Data.Entities;
using Awana.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Awana.Data.Seeding;

/// <summary>
/// Brings the database up to the reference data the application needs.
///
/// Idempotent by construction: every step looks the row up by its natural key
/// and creates it only if absent. Running this on every boot is safe, which
/// matters because that is exactly what happens on every deploy.
///
/// It deliberately does NOT overwrite. If someone renames a team or edits the
/// scoring profile through the app, a later deploy leaves that alone. The
/// seeder establishes a starting point; it is not a source of truth that
/// fights the user.
/// </summary>
public class DbSeeder(AwanaDbContext db, ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(SeedOptions options, CancellationToken ct = default)
    {
        var church = await SeedChurchAsync(options, ct);
        var divisions = await SeedDivisionsAsync(church, ct);
        await SeedTeamsAsync(church, divisions, ct);
        await SeedScoringProfileAsync(church, ct);
        await SeedGamesAsync(church, ct);
        await SeedUsersAsync(church, options, ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task<Church> SeedChurchAsync(SeedOptions options, CancellationToken ct)
    {
        var church = await db.Churches.FirstOrDefaultAsync(c => c.Slug == options.ChurchSlug, ct);

        if (church is not null) return church;

        church = new Church
        {
            Name = options.ChurchName,
            Slug = options.ChurchSlug,
            TimeZoneId = options.TimeZoneId,
        };

        db.Churches.Add(church);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded church {Slug}", church.Slug);
        return church;
    }

    private async Task<Dictionary<string, Division>> SeedDivisionsAsync(Church church, CancellationToken ct)
    {
        var existing = await db.Divisions
            .Where(d => d.ChurchId == church.Id)
            .ToDictionaryAsync(d => d.Slug, ct);

        foreach (var seed in SeedCatalog.Divisions)
        {
            if (existing.ContainsKey(seed.Slug)) continue;

            var division = new Division
            {
                ChurchId = church.Id,
                Name = seed.Name,
                Slug = seed.Slug,
                SortOrder = seed.SortOrder,
            };

            db.Divisions.Add(division);
            existing[seed.Slug] = division;
            logger.LogInformation("Seeded division {Slug}", seed.Slug);
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }

    private async Task SeedTeamsAsync(
        Church church, Dictionary<string, Division> divisions, CancellationToken ct)
    {
        var existing = await db.Teams
            .Where(t => t.ChurchId == church.Id)
            .Select(t => new { t.DivisionId, t.Name })
            .ToListAsync(ct);

        var present = existing.Select(t => (t.DivisionId, t.Name)).ToHashSet();

        foreach (var division in divisions.Values)
        {
            foreach (var seed in SeedCatalog.Teams)
            {
                if (present.Contains((division.Id, seed.Name))) continue;

                db.Teams.Add(new Team
                {
                    ChurchId = church.Id,
                    DivisionId = division.Id,
                    Name = seed.Name,
                    ColorHex = seed.ColorHex,
                    TextOnColorHex = seed.TextOnColorHex,
                    SortOrder = seed.SortOrder,
                });

                logger.LogInformation("Seeded team {Division}/{Team}", division.Slug, seed.Name);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The official table, which the church cannot edit. See ScoringProfile.
    /// </summary>
    private async Task SeedScoringProfileAsync(Church church, CancellationToken ct)
    {
        var keyed = await db.ScoringProfiles
            .AnyAsync(p => p.ChurchId == church.Id && p.SeedKey == SeedCatalog.DefaultProfileKey, ct);

        if (keyed) return;

        // Adopted by name once, for a row seeded before the key existed. The
        // only time a name is consulted.
        var unkeyed = await db.ScoringProfiles.FirstOrDefaultAsync(
            p => p.ChurchId == church.Id && p.SeedKey == null && p.Name == SeedCatalog.DefaultProfileName,
            ct);

        if (unkeyed is not null)
        {
            unkeyed.SeedKey = SeedCatalog.DefaultProfileKey;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Adopted scoring profile {Name} as the seeded one", unkeyed.Name);
            return;
        }

        db.ScoringProfiles.Add(new ScoringProfile
        {
            ChurchId = church.Id,
            Name = SeedCatalog.DefaultProfileName,
            Config = ScoringConfig.Default,
            IsDefault = true,
            SeedKey = SeedCatalog.DefaultProfileKey,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded scoring profile {Name}", SeedCatalog.DefaultProfileName);
    }

    /// <summary>
    /// The game catalog, which after the first boot belongs to the church.
    /// </summary>
    /// <remarks>
    /// Matched on <see cref="Game.SeedKey"/>, never on name. A church renames
    /// these, retires them and adds its own, and matching on something visible
    /// would mean a rename looked to the next boot like a missing game and got
    /// a duplicate created beside it.
    ///
    /// Rows seeded before SeedKey existed are adopted by name once, which is
    /// the only time a name is consulted. A game sharing its name with a seed
    /// is that seed, and the alternative is to duplicate it.
    /// </remarks>
    private async Task SeedGamesAsync(Church church, CancellationToken ct)
    {
        var existing = await db.Games
            .Where(g => g.ChurchId == church.Id)
            .ToListAsync(ct);

        var byKey = existing
            .Where(g => g.SeedKey is not null)
            .ToDictionary(g => g.SeedKey!);

        var unkeyedByName = existing
            .Where(g => g.SeedKey is null)
            .ToDictionary(g => g.Name);

        foreach (var seed in SeedCatalog.Games)
        {
            if (byKey.ContainsKey(seed.Key)) continue;

            if (unkeyedByName.TryGetValue(seed.Name, out var adopted))
            {
                adopted.SeedKey = seed.Key;

                // Filling a blank, which is not the same as overwriting. This
                // runs once, at the moment a pre-SeedKey row is adopted, so a
                // note somebody later clears on purpose stays cleared.
                adopted.Notes ??= seed.Notes;

                logger.LogInformation("Adopted existing game {Game} as seed {Key}", seed.Name, seed.Key);
                continue;
            }

            db.Games.Add(new Game
            {
                ChurchId = church.Id,
                Name = seed.Name,
                Notes = seed.Notes,
                SortOrder = seed.SortOrder,
                SeedKey = seed.Key,
            });

            logger.LogInformation("Seeded game {Game}", seed.Name);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Creates the accounts configuration names, and restores configured admins.
    /// </summary>
    /// <remarks>
    /// Roles belong to the People page now, so this no longer rewrites them.
    /// It used to set every listed account's role on every boot, which was
    /// right while configuration was the only way to manage anyone and wrong
    /// the moment there was another: a promotion made in the app would have
    /// been quietly reverted by the next deploy.
    ///
    /// The admin list is the exception, on purpose. Its addresses are put back
    /// to active admins on every boot, so that demoting or deactivating every
    /// admin in the app is recoverable by restarting the server rather than by
    /// editing production with SQL.
    /// </remarks>
    private async Task SeedUsersAsync(Church church, SeedOptions options, CancellationToken ct)
    {
        // Highest role last, so someone listed twice is created with the
        // greater of the two rather than depending on list order.
        var allowlists = new (UserRole Role, string Raw)[]
        {
            (UserRole.Scorekeeper, options.ScorekeeperEmails),
            (UserRole.GamesLeader, options.GamesLeaderEmails),
            (UserRole.Admin, options.AdminEmails),
        };

        var intended = new Dictionary<string, UserRole>(StringComparer.Ordinal);

        foreach (var (role, raw) in allowlists)
        {
            foreach (var email in SeedOptions.ParseEmails(raw))
            {
                if (!intended.TryGetValue(email, out var already) || role > already)
                {
                    intended[email] = role;
                }
            }
        }

        var existing = await db.Users
            .Where(u => u.ChurchId == church.Id)
            .ToDictionaryAsync(u => u.Email, ct);

        if (intended.Count == 0 && existing.Count == 0)
        {
            logger.LogWarning(
                "No users. Nobody can sign in until Seed__AdminEmails is configured.");
            return;
        }

        foreach (var (email, role) in intended)
        {
            if (existing.TryGetValue(email, out var user))
            {
                if (role == UserRole.Admin && (user.Role != UserRole.Admin || !user.IsActive))
                {
                    // Warning rather than information: this undoes something an
                    // admin did on purpose, and whoever did it should be able to
                    // find out why it came back.
                    logger.LogWarning(
                        "Restored {Email} to an active admin because it is listed in Seed__AdminEmails",
                        email);

                    user.Role = UserRole.Admin;
                    user.IsActive = true;
                }

                // Everyone else keeps whatever the People page gave them. The
                // display name is not touched either: Google supplies it at
                // sign-in and that is better than a guess.
                continue;
            }

            db.Users.Add(new AppUser
            {
                ChurchId = church.Id,
                Email = email,
                DisplayName = email,
                Role = role,
            });

            logger.LogInformation("Seeded user {Email} as {Role}", email, role);
        }

        await db.SaveChangesAsync(ct);
    }
}
