using System.Text.Json;
using Awana.Api.Contracts;
using Awana.Data;
using Awana.Data.Entities;
using Awana.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Services;

/// <summary>
/// The named sets of scoring rules a church can choose between.
/// </summary>
/// <remarks>
/// Editing these is safe for history by construction, and that is worth being
/// explicit about because it is the whole reason the design looks like this. A
/// session takes a FROZEN COPY of its profile's config the moment it starts, so
/// changing a profile in December cannot alter what October was worth. The
/// profile is the template; the session owns the rules it actually ran under.
///
/// Which means the only genuinely destructive action here is deleting a profile
/// outright, and even that leaves finished sessions scoring correctly.
/// </remarks>
public class ScoringProfileService(AwanaDbContext db, IScoringEngine engine)
{
    private const int MaxName = 120;

    /// <summary>A ceiling on how many sets of rules one church can keep.</summary>
    private const int MaxProfiles = 50;

    public async Task<IReadOnlyList<ScoringProfileDto>> ListAsync(
        Guid churchId, CancellationToken ct = default)
    {
        var profiles = await db.ScoringProfiles
            .AsNoTracking()
            .Where(p => p.ChurchId == churchId)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        // Counted per profile rather than joined, because the list is tiny and
        // the query stays obvious.
        var usage = await db.Sessions
            .Where(s => s.ChurchId == churchId && s.ScoringProfileId != null)
            .GroupBy(s => s.ScoringProfileId!.Value)
            .Select(g => new { ProfileId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProfileId, x => x.Count, ct);

        return profiles
            .Select(p => new ScoringProfileDto(
                p.Id,
                p.Name,
                p.Config,
                p.IsDefault,
                p.IsActive,
                usage.GetValueOrDefault(p.Id),
                p.SeedKey is not null))
            .ToList();
    }

    public async Task<ServiceResult<ScoringProfileDto>> CreateAsync(
        Guid churchId, SaveScoringProfileRequest request, Guid? userId, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(churchId, request, existingId: null, ct);
        if (validation is not null) return ServiceResult<ScoringProfileDto>.Fail(validation);

        var count = await db.ScoringProfiles.CountAsync(p => p.ChurchId == churchId, ct);
        if (count >= MaxProfiles)
        {
            return ServiceResult<ScoringProfileDto>.Fail(
                ServiceError.Invalid($"A church cannot have more than {MaxProfiles} sets of rules."));
        }

        // The first set of rules a church has is necessarily the default, since
        // a session starting with no default would have nothing to freeze.
        var isFirst = count == 0;

        var profile = new ScoringProfile
        {
            ChurchId = churchId,
            Name = request.Name.Trim(),
            Config = request.Config,
            IsDefault = isFirst,
        };

        db.ScoringProfiles.Add(profile);

        AddAudit(churchId, userId, "scoring_profile.created", profile.Id, new
        {
            profile.Name,
            request.Config.PlacePoints,
            request.Config.TieRule,
            request.Config.DqRule,
        });

        await db.SaveChangesAsync(ct);

        return ServiceResult<ScoringProfileDto>.Success(await DetailAsync(churchId, profile.Id, ct));
    }

    /// <summary>
    /// Edits a set of rules in place.
    /// </summary>
    /// <remarks>
    /// Sessions already played are untouched, because each froze its own copy
    /// when it started. A session still in setup has not frozen anything yet
    /// and will pick this up when it starts, which is the intended behavior:
    /// setup means the night has not begun.
    /// </remarks>
    public async Task<ServiceResult<ScoringProfileDto>> UpdateAsync(
        Guid churchId, Guid id, SaveScoringProfileRequest request, Guid? userId,
        CancellationToken ct = default)
    {
        var profile = await Find(churchId, id, ct);
        if (profile is null) return ServiceResult<ScoringProfileDto>.Fail(ServiceError.NotFound("Scoring rules"));

        if (profile.SeedKey is not null)
        {
            return ServiceResult<ScoringProfileDto>.Fail(ServiceError.Conflict(
                "scoring_profile_is_seeded",
                $"{profile.Name} is the standard table and cannot be changed. Duplicate it to make a set you can edit."));
        }

        var validation = await ValidateAsync(churchId, request, existingId: id, ct);
        if (validation is not null) return ServiceResult<ScoringProfileDto>.Fail(validation);

        var before = new { profile.Name, profile.Config };

        profile.Name = request.Name.Trim();
        profile.Config = request.Config;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        AddAudit(churchId, userId, "scoring_profile.updated", profile.Id, new
        {
            Before = before,
            After = new { profile.Name, profile.Config },
        });

        await db.SaveChangesAsync(ct);

        return ServiceResult<ScoringProfileDto>.Success(await DetailAsync(churchId, profile.Id, ct));
    }

    /// <summary>
    /// Makes one set of rules the one new sessions get when nobody chooses.
    /// </summary>
    public async Task<ServiceResult<IReadOnlyList<ScoringProfileDto>>> SetDefaultAsync(
        Guid churchId, Guid id, Guid? userId, CancellationToken ct = default)
    {
        var profile = await Find(churchId, id, ct);
        if (profile is null)
        {
            return ServiceResult<IReadOnlyList<ScoringProfileDto>>.Fail(
                ServiceError.NotFound("Scoring rules"));
        }

        if (!profile.IsActive)
        {
            return ServiceResult<IReadOnlyList<ScoringProfileDto>>.Fail(ServiceError.Conflict(
                "scoring_profile_retired",
                "These rules are retired. Put them back before making them the default."));
        }

        // Loaded and cleared rather than updated in bulk, so that exactly one
        // row can carry the flag no matter how it got into its current state.
        var all = await db.ScoringProfiles.Where(p => p.ChurchId == churchId).ToListAsync(ct);

        foreach (var other in all) other.IsDefault = other.Id == id;

        AddAudit(churchId, userId, "scoring_profile.default_set", profile.Id, new { profile.Name });
        await db.SaveChangesAsync(ct);

        return ServiceResult<IReadOnlyList<ScoringProfileDto>>.Success(await ListAsync(churchId, ct));
    }

    /// <summary>
    /// Takes a set of rules out of the list new sessions choose from.
    /// </summary>
    /// <remarks>
    /// The same idea as retiring a game: it stops being offered and everything
    /// that used it is left alone. The default cannot be retired, because a
    /// church with no default has nothing to start a session with.
    /// </remarks>
    public async Task<ServiceResult<ScoringProfileDto>> SetActiveAsync(
        Guid churchId, Guid id, bool isActive, Guid? userId, CancellationToken ct = default)
    {
        var profile = await Find(churchId, id, ct);
        if (profile is null) return ServiceResult<ScoringProfileDto>.Fail(ServiceError.NotFound("Scoring rules"));

        if (!isActive && profile.IsDefault)
        {
            return ServiceResult<ScoringProfileDto>.Fail(ServiceError.Conflict(
                "scoring_profile_is_default",
                "These are the default rules, so they cannot be retired. Make another set the default first."));
        }

        if (profile.IsActive != isActive)
        {
            profile.IsActive = isActive;
            profile.UpdatedAt = DateTimeOffset.UtcNow;

            AddAudit(
                churchId, userId,
                isActive ? "scoring_profile.restored" : "scoring_profile.retired",
                profile.Id, new { profile.Name });

            await db.SaveChangesAsync(ct);
        }

        return ServiceResult<ScoringProfileDto>.Success(await DetailAsync(churchId, profile.Id, ct));
    }

    /// <summary>
    /// Only ever a set of rules no session has used, and never the default.
    /// </summary>
    /// <remarks>
    /// A session names the profile it was started from, so deleting one that
    /// has been used would break that reference. The session would still score
    /// correctly, because it froze its own copy, but it could no longer say
    /// where its rules came from. Retiring keeps that answer.
    /// </remarks>
    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid churchId, Guid id, Guid? userId, CancellationToken ct = default)
    {
        var profile = await Find(churchId, id, ct);
        if (profile is null) return ServiceResult<bool>.Fail(ServiceError.NotFound("Scoring rules"));

        if (profile.SeedKey is not null)
        {
            return ServiceResult<bool>.Fail(ServiceError.Conflict(
                "scoring_profile_is_seeded",
                $"{profile.Name} is the standard table and cannot be deleted. Retire it if it is not wanted."));
        }

        if (profile.IsDefault)
        {
            return ServiceResult<bool>.Fail(ServiceError.Conflict(
                "scoring_profile_is_default",
                "These are the default rules. Make another set the default first."));
        }

        var used = await db.Sessions.CountAsync(s => s.ScoringProfileId == id, ct);

        if (used > 0)
        {
            return ServiceResult<bool>.Fail(ServiceError.Conflict(
                "scoring_profile_in_use",
                $"{used} {(used == 1 ? "session has" : "sessions have")} been run on these rules, so deleting them would leave those sessions unable to say where their scoring came from. Retire them instead."));
        }

        AddAudit(churchId, userId, "scoring_profile.deleted", profile.Id, new
        {
            profile.Name,
            profile.Config,
        });

        db.ScoringProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);

        return ServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Copies a set of rules into a new one the church can edit.
    /// </summary>
    /// <remarks>
    /// The way anything gets changed about the standard table, and the usual
    /// way a second set is made at all: a tournament table is the official one
    /// with a number altered rather than something built from nothing.
    /// </remarks>
    public async Task<ServiceResult<ScoringProfileDto>> DuplicateAsync(
        Guid churchId, Guid id, Guid? userId, CancellationToken ct = default)
    {
        var source = await Find(churchId, id, ct);
        if (source is null) return ServiceResult<ScoringProfileDto>.Fail(ServiceError.NotFound("Scoring rules"));

        var count = await db.ScoringProfiles.CountAsync(p => p.ChurchId == churchId, ct);
        if (count >= MaxProfiles)
        {
            return ServiceResult<ScoringProfileDto>.Fail(
                ServiceError.Invalid($"A church cannot have more than {MaxProfiles} sets of rules."));
        }

        var taken = await db.ScoringProfiles
            .Where(p => p.ChurchId == churchId)
            .Select(p => p.Name)
            .ToListAsync(ct);

        var copy = new ScoringProfile
        {
            ChurchId = churchId,
            Name = UniqueName($"{source.Name} copy", taken),
            Config = source.Config,
            SchemaVersion = source.SchemaVersion,
            // Never the default and never seeded: it is a new set that happens
            // to start with somebody else's numbers.
            IsDefault = false,
        };

        db.ScoringProfiles.Add(copy);

        AddAudit(churchId, userId, "scoring_profile.duplicated", copy.Id, new
        {
            From = source.Name,
            copy.Name,
        });

        await db.SaveChangesAsync(ct);

        return ServiceResult<ScoringProfileDto>.Success(await DetailAsync(churchId, copy.Id, ct));
    }

    /// <summary>
    /// What a set of rules would actually do, worked through on four rounds.
    /// </summary>
    /// <remarks>
    /// The point of the editor, really. Tie rules and DQ rules are the two
    /// settings nobody can predict the effect of by reading their names, and
    /// this answers it with the same engine that will score Friday rather than
    /// with a second implementation in the browser that would eventually drift.
    ///
    /// Nothing is written and no profile need exist, so an unsaved edit can be
    /// checked before committing to it.
    /// </remarks>
    public ServiceResult<ScoringPreviewDto> Preview(ScoringConfig config)
    {
        var validation = engine.ValidateConfig(config);

        if (!validation.IsValid)
        {
            return ServiceResult<ScoringPreviewDto>.Fail(new ServiceError(
                400,
                "invalid_scoring_config",
                "These rules cannot be used yet.",
                validation.Errors.Select(e => new ValidationErrorDto(e.Code, e.Message)).ToList()));
        }

        // Four fixed teams, so the examples read like a real round rather than
        // like abstract slots. Named rather than colored: this is about the
        // rules, and using real team names would imply these results happened.
        var names = new[] { "Team A", "Team B", "Team C", "Team D" };
        var ids = names.Select(_ => Guid.CreateVersion7()).ToArray();

        TeamEntry Entry(int index, int? place, bool dq = false) =>
            new(ids[index], place, dq, 0m, names[index]);

        var cases = new (string Title, string Question, RoundInput Input)[]
        {
            ("A clean round",
             "Nobody tied, nobody disqualified.",
             new RoundInput([Entry(0, 1), Entry(1, 2), Entry(2, 3), Entry(3, 4)])),

            ("Two teams tie for first",
             "What the tie rule does when the top two finish level.",
             new RoundInput([Entry(0, 1), Entry(1, 1), Entry(2, 2), Entry(3, 3)])),

            ("All four tie",
             "The extreme case, and the one that catches a tie rule out.",
             new RoundInput([Entry(0, 1), Entry(1, 1), Entry(2, 1), Entry(3, 1)])),

            ("The winner is disqualified",
             "What the DQ rule does, and whether second place inherits the win.",
             new RoundInput([Entry(0, 1, dq: true), Entry(1, 2), Entry(2, 3), Entry(3, 4)])),
        };

        var worked = new List<ScoringExampleDto>();

        foreach (var (title, question, input) in cases)
        {
            // A rule can legitimately reject a shape rather than score it:
            // NoTiesAllowed makes the tie examples invalid, which is itself the
            // most useful thing the preview can show for that setting.
            var roundCheck = engine.Validate(input, config);

            if (!roundCheck.IsValid)
            {
                worked.Add(new ScoringExampleDto(
                    title, question, [], roundCheck.Errors[0].Message));
                continue;
            }

            var outcome = engine.Score(input, config);

            worked.Add(new ScoringExampleDto(
                title,
                question,
                outcome.Awards
                    .Select(a => new ScoringExampleTeamDto(
                        names[Array.IndexOf(ids, a.TeamId)],
                        a.Place,
                        a.IsDisqualified,
                        a.Points,
                        a.Explanation))
                    .ToList(),
                null));
        }

        return ServiceResult<ScoringPreviewDto>.Success(new ScoringPreviewDto(worked));
    }

    // --------------------------------------------------------------- helpers

    /// <summary>Appends a number until the name is free, as a file manager does.</summary>
    private static string UniqueName(string basis, IReadOnlyCollection<string> taken)
    {
        var trimmed = basis.Length > MaxName ? basis[..MaxName] : basis;

        if (!taken.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return trimmed;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{trimmed} {n}";
            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }

        return $"{trimmed} {Guid.NewGuid():N}"[..MaxName];
    }

    private Task<ScoringProfile?> Find(Guid churchId, Guid id, CancellationToken ct) =>
        db.ScoringProfiles.FirstOrDefaultAsync(p => p.Id == id && p.ChurchId == churchId, ct);

    private async Task<ScoringProfileDto> DetailAsync(Guid churchId, Guid id, CancellationToken ct)
    {
        var profile = await db.ScoringProfiles.AsNoTracking().FirstAsync(p => p.Id == id, ct);
        var used = await db.Sessions.CountAsync(s => s.ScoringProfileId == id, ct);

        return new ScoringProfileDto(
            profile.Id, profile.Name, profile.Config, profile.IsDefault, profile.IsActive, used,
            profile.SeedKey is not null);
    }

    private async Task<ServiceError?> ValidateAsync(
        Guid churchId, SaveScoringProfileRequest request, Guid? existingId, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0) return ServiceError.Invalid("These rules need a name.");

        if (name.Length > MaxName)
        {
            return ServiceError.Invalid($"That name is longer than {MaxName} characters.");
        }

        // The engine is the authority on whether it can run these.
        var config = engine.ValidateConfig(request.Config);

        if (!config.IsValid)
        {
            return new ServiceError(
                400,
                "invalid_scoring_config",
                config.Errors[0].Message,
                config.Errors.Select(e => new ValidationErrorDto(e.Code, e.Message)).ToList());
        }

        var duplicate = await db.ScoringProfiles.AnyAsync(
            p => p.ChurchId == churchId && p.Id != existingId && p.Name.ToLower() == name.ToLower(), ct);

        if (duplicate)
        {
            return ServiceError.Conflict(
                "scoring_profile_name_taken", $"There is already a set of rules called {name}.");
        }

        return null;
    }

    private void AddAudit(Guid churchId, Guid? userId, string action, Guid entityId, object data) =>
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = churchId,
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(ScoringProfile),
            EntityId = entityId,
            Data = JsonSerializer.SerializeToDocument(data),
        });
}
