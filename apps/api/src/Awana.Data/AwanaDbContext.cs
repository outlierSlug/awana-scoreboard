using System.Text.Json;
using Awana.Data.Entities;
using Awana.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Awana.Data;

public class AwanaDbContext(DbContextOptions<AwanaDbContext> options) : DbContext(options)
{
    public DbSet<Church> Churches => Set<Church>();
    public DbSet<ScoringProfile> ScoringProfiles => Set<ScoringProfile>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionTeam> SessionTeams => Set<SessionTeam>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<RoundResult> RoundResults => Set<RoundResult>();
    public DbSet<PointAdjustment> PointAdjustments => Set<PointAdjustment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// The scoring config is a document: read whole, never queried by field.
    /// Web defaults so the stored JSON uses the same camelCase the API emits,
    /// which makes a row readable next to a response body.
    /// </summary>
    private static readonly JsonSerializerOptions ConfigJson = new(JsonSerializerDefaults.Web);

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        ConfigureConventions(b);

        ConfigureChurch(b);
        ConfigureScoringProfile(b);
        ConfigureUser(b);
        ConfigureDivision(b);
        ConfigureTeam(b);
        ConfigureGame(b);
        ConfigureSession(b);
        ConfigureSessionTeam(b);
        ConfigureRound(b);
        ConfigureRoundResult(b);
        ConfigurePointAdjustment(b);
        ConfigureAuditLog(b);
    }

    private static void ConfigureConventions(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            // Every money-like column is the same shape. Three decimal places
            // is more than the rules can currently produce, which leaves room
            // for a church whose table divides unevenly.
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetColumnType("numeric(9,3)");
                }
            }

            // Restrict is the default, and cascade is opted into explicitly
            // below for the two places it is correct. Deleting a team must
            // never silently erase the rounds it played in.
            foreach (var fk in entity.GetForeignKeys())
            {
                fk.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

    // ------------------------------------------------------------- catalog

    private static void ConfigureChurch(ModelBuilder b) => b.Entity<Church>(e =>
    {
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Slug).HasMaxLength(64);
        e.Property(x => x.TimeZoneId).HasMaxLength(64);
        e.HasIndex(x => x.Slug).IsUnique();
    });

    private static void ConfigureScoringProfile(ModelBuilder b) => b.Entity<ScoringProfile>(e =>
    {
        e.Property(x => x.Name).HasMaxLength(120);

        // The document, as jsonb.
        //
        // The comparer matters as much as the converter: without one, EF
        // compares this by reference, so an edit to the config of a tracked
        // profile is silently not saved.
        var converter = new ValueConverter<ScoringConfig, string>(
            v => JsonSerializer.Serialize(v, ConfigJson),
            v => JsonSerializer.Deserialize<ScoringConfig>(v, ConfigJson)!);

        var comparer = new ValueComparer<ScoringConfig>(
            (l, r) => JsonSerializer.Serialize(l, ConfigJson) == JsonSerializer.Serialize(r, ConfigJson),
            v => JsonSerializer.Serialize(v, ConfigJson).GetHashCode(),
            v => JsonSerializer.Deserialize<ScoringConfig>(JsonSerializer.Serialize(v, ConfigJson), ConfigJson)!);

        e.Property(x => x.Config)
            .HasConversion(converter, comparer)
            .HasColumnType("jsonb")
            .HasColumnName("config_json");

        e.HasIndex(x => new { x.ChurchId, x.Name }).IsUnique();

        e.HasOne(x => x.Church).WithMany(c => c.ScoringProfiles).HasForeignKey(x => x.ChurchId);
    });

    private static void ConfigureUser(ModelBuilder b) => b.Entity<AppUser>(e =>
    {
        e.Property(x => x.Email).HasMaxLength(320);
        e.Property(x => x.DisplayName).HasMaxLength(200);
        e.Property(x => x.GoogleSubject).HasMaxLength(64);

        e.HasIndex(x => new { x.ChurchId, x.Email }).IsUnique();

        // Filtered, because the column stays null until a seeded user signs in
        // for the first time and Postgres would otherwise allow only one null.
        e.HasIndex(x => x.GoogleSubject).IsUnique().HasFilter("google_subject IS NOT NULL");

        e.HasOne(x => x.Church).WithMany(c => c.Users).HasForeignKey(x => x.ChurchId);
    });

    private static void ConfigureDivision(ModelBuilder b) => b.Entity<Division>(e =>
    {
        e.Property(x => x.Name).HasMaxLength(80);
        e.Property(x => x.Slug).HasMaxLength(32);
        e.HasIndex(x => new { x.ChurchId, x.Slug }).IsUnique();
        e.HasOne(x => x.Church).WithMany(c => c.Divisions).HasForeignKey(x => x.ChurchId);
    });

    private static void ConfigureTeam(ModelBuilder b) => b.Entity<Team>(e =>
    {
        e.Property(x => x.Name).HasMaxLength(80);
        e.Property(x => x.ColorHex).HasMaxLength(9);
        e.Property(x => x.TextOnColorHex).HasMaxLength(9);

        e.HasIndex(x => new { x.ChurchId, x.DivisionId, x.Name }).IsUnique();

        e.HasOne(x => x.Church).WithMany().HasForeignKey(x => x.ChurchId);
        e.HasOne(x => x.Division).WithMany(d => d.Teams).HasForeignKey(x => x.DivisionId);
    });

    private static void ConfigureGame(ModelBuilder b) => b.Entity<Game>(e =>
    {
        e.Property(x => x.Name).HasMaxLength(120);
        e.HasIndex(x => new { x.ChurchId, x.Name }).IsUnique();

        e.HasOne(x => x.Church).WithMany(c => c.Games).HasForeignKey(x => x.ChurchId);
        e.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId);
    });

    // ------------------------------------------------------------- sessions

    private static void ConfigureSession(ModelBuilder b) => b.Entity<Session>(e =>
    {
        e.Property(x => x.PublicSlug).HasMaxLength(120);
        e.HasIndex(x => x.PublicSlug).IsUnique();

        // The board asks "what is live right now" on every cold load.
        e.HasIndex(x => new { x.ChurchId, x.Status });
        e.HasIndex(x => new { x.ChurchId, x.DivisionId, x.Date });

        // The frozen snapshot. Nullable, because it does not exist until the
        // session starts.
        var converter = new ValueConverter<ScoringConfig, string>(
            v => JsonSerializer.Serialize(v, ConfigJson),
            v => JsonSerializer.Deserialize<ScoringConfig>(v, ConfigJson)!);

        var comparer = new ValueComparer<ScoringConfig>(
            (l, r) => JsonSerializer.Serialize(l, ConfigJson) == JsonSerializer.Serialize(r, ConfigJson),
            v => JsonSerializer.Serialize(v, ConfigJson).GetHashCode(),
            v => JsonSerializer.Deserialize<ScoringConfig>(JsonSerializer.Serialize(v, ConfigJson), ConfigJson)!);

        e.Property(x => x.ScoringConfig)
            .HasConversion(converter!, comparer!)
            .HasColumnType("jsonb")
            .HasColumnName("scoring_config_json");

        // Postgres xmin. Two writers cannot silently overwrite each other.
        e.Property(x => x.RowVersion).IsRowVersion();

        e.HasOne(x => x.Church).WithMany(c => c.Sessions).HasForeignKey(x => x.ChurchId);
        e.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId);
        e.HasOne(x => x.ScoringProfile).WithMany().HasForeignKey(x => x.ScoringProfileId);
        e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId);
    });

    private static void ConfigureSessionTeam(ModelBuilder b) => b.Entity<SessionTeam>(e =>
    {
        e.HasIndex(x => new { x.SessionId, x.TeamId }).IsUnique();

        // Part of the session aggregate: deleting a session takes these too.
        e.HasOne(x => x.Session).WithMany(s => s.SessionTeams)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId);
    });

    private static void ConfigureRound(ModelBuilder b) => b.Entity<Round>(e =>
    {
        e.Property(x => x.VoidReason).HasMaxLength(500);

        // The database-level backstop for round numbering. Filtered on
        // voided_at so that voiding round 3 frees the number for its rerun.
        e.HasIndex(x => new { x.SessionId, x.RoundNumber })
            .IsUnique()
            .HasFilter("voided_at IS NULL");

        // What makes a retried POST idempotent rather than a second score.
        e.HasIndex(x => new { x.SessionId, x.ClientRequestId }).IsUnique();

        e.HasIndex(x => new { x.ChurchId, x.SessionId });

        e.HasOne(x => x.Session).WithMany(s => s.Rounds)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.Church).WithMany().HasForeignKey(x => x.ChurchId);
        e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId);
        e.HasOne(x => x.RecordedByUser).WithMany().HasForeignKey(x => x.RecordedByUserId);
        e.HasOne(x => x.VoidedByUser).WithMany().HasForeignKey(x => x.VoidedByUserId);
    });

    private static void ConfigureRoundResult(ModelBuilder b) => b.Entity<RoundResult>(e =>
    {
        e.Property(x => x.Explanation).HasMaxLength(1000);
        e.Property(x => x.BonusReason).HasMaxLength(200);

        e.HasIndex(x => new { x.RoundId, x.TeamId }).IsUnique();

        // For cross-session team statistics after launch.
        e.HasIndex(x => x.TeamId);

        e.HasOne(x => x.Round).WithMany(r => r.Results)
            .HasForeignKey(x => x.RoundId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId);
    });

    private static void ConfigurePointAdjustment(ModelBuilder b) => b.Entity<PointAdjustment>(e =>
    {
        e.Property(x => x.Reason).HasMaxLength(500);
        e.HasIndex(x => new { x.ChurchId, x.SessionId });

        e.HasOne(x => x.Session).WithMany(s => s.PointAdjustments)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(x => x.Church).WithMany().HasForeignKey(x => x.ChurchId);
        e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId);
        e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId);
        e.HasOne(x => x.VoidedByUser).WithMany().HasForeignKey(x => x.VoidedByUserId);
    });

    private static void ConfigureAuditLog(ModelBuilder b) => b.Entity<AuditLog>(e =>
    {
        e.Property(x => x.Action).HasMaxLength(80);
        e.Property(x => x.EntityType).HasMaxLength(80);
        e.Property(x => x.Data).HasColumnType("jsonb");

        e.HasIndex(x => new { x.ChurchId, x.CreatedAt });

        e.HasOne(x => x.Church).WithMany().HasForeignKey(x => x.ChurchId);
        e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId);
    });
}
