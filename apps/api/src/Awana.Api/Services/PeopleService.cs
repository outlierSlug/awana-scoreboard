using System.Net.Mail;
using System.Text.Json;
using Awana.Api.Contracts;
using Awana.Data;
using Awana.Data.Entities;
using Awana.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Awana.Api.Services;

/// <summary>
/// Who can sign in, what each of them may do, and what has been done.
/// </summary>
/// <remarks>
/// The database is the source of truth for roles. Configuration only creates
/// accounts and keeps the listed admins as a way back in; see
/// <see cref="SeedOptions.AdminEmails"/>.
///
/// Nobody is ever deleted. Rounds, adjustments and the audit log all point at
/// the person who made them, and a season's history has to keep saying who
/// that was after they stop volunteering. Deactivating is the way out.
/// </remarks>
public class PeopleService(AwanaDbContext db, IOptions<SeedOptions> seed)
{
    /// <summary>Matches the column, so an absurd address is a 400 and not a 500.</summary>
    private const int MaxEmail = 320;

    private const int DefaultActivityPage = 50;
    private const int MaxActivityPage = 100;

    public async Task<IReadOnlyList<PersonDto>> ListAsync(Guid churchId, CancellationToken ct = default)
    {
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.ChurchId == churchId)
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.DisplayName)
            .ToListAsync(ct);

        var configAdmins = seed.Value.ConfiguredAdmins();

        return users.Select(u => ToDto(u, configAdmins)).ToList();
    }

    public async Task<ServiceResult<PersonDto>> AddAsync(
        Guid churchId, AddPersonRequest request, Guid? actorId, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null)
        {
            return ServiceResult<PersonDto>.Fail(ServiceError.Invalid(
                "That does not look like an email address. Use the Google account they will sign in with."));
        }

        if (ParseRole(request.Role) is not { } role)
        {
            return ServiceResult<PersonDto>.Fail(ServiceError.Invalid("Choose a role for them."));
        }

        var existing = await db.Users.FirstOrDefaultAsync(
            u => u.ChurchId == churchId && u.Email == email, ct);

        if (existing is not null)
        {
            return ServiceResult<PersonDto>.Fail(ServiceError.Conflict(
                "person_exists",
                existing.IsActive
                    ? $"{email} already has an account. Change its role instead."
                    : $"{email} already has an account that was deactivated. Reactivate it instead."));
        }

        var user = new AppUser
        {
            ChurchId = churchId,
            Email = email,
            // Replaced with their real name the first time they sign in. Until
            // then the address is the only honest thing to call them.
            DisplayName = email,
            Role = role,
        };

        db.Users.Add(user);

        AddAudit(churchId, actorId, "person.added", user.Id, new { user.Email, Role = role.ToString() });

        await db.SaveChangesAsync(ct);

        return ServiceResult<PersonDto>.Success(ToDto(user, seed.Value.ConfiguredAdmins()));
    }

    /// <summary>
    /// Changes what someone may do. Takes effect on their next request, not their
    /// next sign-in: the session is re-checked against this table every time.
    /// </summary>
    public async Task<ServiceResult<PersonDto>> SetRoleAsync(
        Guid churchId, Guid id, SetRoleRequest request, Guid? actorId, CancellationToken ct = default)
    {
        if (ParseRole(request.Role) is not { } role)
        {
            return ServiceResult<PersonDto>.Fail(ServiceError.Invalid("That is not a role."));
        }

        var user = await Find(churchId, id, ct);
        if (user is null) return ServiceResult<PersonDto>.Fail(ServiceError.NotFound("Person"));

        if (await RefuseChangeAsync(user, actorId, removesAdmin: user.Role == UserRole.Admin && role != UserRole.Admin, ct)
            is { } refusal)
        {
            return ServiceResult<PersonDto>.Fail(refusal);
        }

        if (user.Role != role)
        {
            var from = user.Role;
            user.Role = role;

            AddAudit(churchId, actorId, "person.role_changed", user.Id, new
            {
                user.Email,
                From = from.ToString(),
                To = role.ToString(),
            });

            await db.SaveChangesAsync(ct);
        }

        return ServiceResult<PersonDto>.Success(ToDto(user, seed.Value.ConfiguredAdmins()));
    }

    /// <summary>
    /// Deactivating signs them out everywhere on their next request and refuses
    /// their next sign-in. Reactivating puts back exactly the role they had.
    /// </summary>
    public async Task<ServiceResult<PersonDto>> SetActiveAsync(
        Guid churchId, Guid id, bool isActive, Guid? actorId, CancellationToken ct = default)
    {
        var user = await Find(churchId, id, ct);
        if (user is null) return ServiceResult<PersonDto>.Fail(ServiceError.NotFound("Person"));

        if (user.IsActive == isActive)
        {
            return ServiceResult<PersonDto>.Success(ToDto(user, seed.Value.ConfiguredAdmins()));
        }

        // Only deactivating needs guarding. Bringing somebody back cannot lock
        // anyone out, and refusing it for the caller's own account would make
        // no sense, since a deactivated account cannot be making the call.
        if (!isActive &&
            await RefuseChangeAsync(user, actorId, removesAdmin: user.Role == UserRole.Admin, ct) is { } refusal)
        {
            return ServiceResult<PersonDto>.Fail(refusal);
        }

        user.IsActive = isActive;

        AddAudit(churchId, actorId, isActive ? "person.reactivated" : "person.deactivated", user.Id, new
        {
            user.Email,
            Role = user.Role.ToString(),
        });

        await db.SaveChangesAsync(ct);

        return ServiceResult<PersonDto>.Success(ToDto(user, seed.Value.ConfiguredAdmins()));
    }

    /// <summary>
    /// The audit log, newest first, with enough resolved around each entry for
    /// a person to read it.
    /// </summary>
    public async Task<ActivityPageDto> ActivityAsync(
        Guid churchId, DateTimeOffset? before, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? DefaultActivityPage, 1, MaxActivityPage);

        var query = db.AuditLogs.AsNoTracking().Where(a => a.ChurchId == churchId);
        if (before is { } cursor) query = query.Where(a => a.CreatedAt < cursor);

        // One more than a page, so whether there is a next page is known
        // without a second count query.
        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(take + 1)
            .Select(a => new
            {
                a.Id,
                a.CreatedAt,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.ActorUserId,
                ActorName = a.ActorUser != null ? a.ActorUser.DisplayName : null,
                a.Data,
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var sessionOf = await SessionsForAsync(
            rows.Select(r => (r.EntityType, r.EntityId)).ToList(), ct);

        var names = await NamesForAsync(rows.Select(r => r.Data), ct);

        var entries = rows.Select(r => new ActivityEntryDto(
                r.Id,
                r.CreatedAt,
                r.Action,
                r.EntityType,
                r.EntityId,
                r.ActorUserId,
                r.ActorName,
                sessionOf(r.EntityType, r.EntityId),
                r.Data?.RootElement.Clone()))
            .ToList();

        return new ActivityPageDto(entries, names, hasMore ? rows[^1].CreatedAt : null);
    }

    // --------------------------------------------------------------- guards

    /// <summary>
    /// The three ways an admin could lock everyone out, or be undone behind
    /// their back, refused with a sentence that says which.
    /// </summary>
    private async Task<ServiceError?> RefuseChangeAsync(
        AppUser target, Guid? actorId, bool removesAdmin, CancellationToken ct)
    {
        // Somebody else has to do it. The honest reason is lockout: an admin
        // demoting themselves by accident on a phone would otherwise need a
        // second admin, or a restart, to undo it.
        if (target.Id == actorId)
        {
            return ServiceError.Conflict(
                "person_is_you",
                "You cannot change your own access. Ask another admin to do it.");
        }

        if (seed.Value.ConfiguredAdmins().Contains(target.Email))
        {
            return ServiceError.Conflict(
                "person_is_config_admin",
                $"{target.Email} is an admin set in the server configuration, so the next restart would undo this. Remove it from Seed__AdminEmails on Render first.");
        }

        if (removesAdmin && target.IsActive)
        {
            var otherAdmins = await db.Users.CountAsync(u =>
                u.ChurchId == target.ChurchId &&
                u.Id != target.Id &&
                u.Role == UserRole.Admin &&
                u.IsActive, ct);

            if (otherAdmins == 0)
            {
                return ServiceError.Conflict(
                    "person_last_admin",
                    "This is the only active admin. Make someone else an admin first.");
            }
        }

        return null;
    }

    // ------------------------------------------------------------- activity

    /// <summary>
    /// Works out which session each entry happened in, so a round being cleared
    /// reads as happening on a particular night.
    /// </summary>
    /// <remarks>
    /// Entries point at different things: a session, a round, or an adjustment.
    /// One wrinkle is load bearing. "adjustment.added" records the session's id
    /// rather than the adjustment's, while "adjustment.voided" records the
    /// adjustment's, so an adjustment id is tried as either.
    ///
    /// A session that has since been deleted simply resolves to nothing; its
    /// own "session.deleted" entry carries its date in the data.
    /// </remarks>
    private async Task<Func<string, Guid, ActivitySessionDto?>> SessionsForAsync(
        IReadOnlyList<(string Type, Guid Id)> entities, CancellationToken ct)
    {
        var roundIds = entities.Where(e => e.Type == nameof(Round)).Select(e => e.Id).Distinct().ToList();
        var adjustmentIds = entities.Where(e => e.Type == nameof(PointAdjustment)).Select(e => e.Id).Distinct().ToList();

        var roundSession = roundIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Rounds.AsNoTracking()
                .Where(r => roundIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.SessionId, ct);

        var adjustmentSession = adjustmentIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.PointAdjustments.AsNoTracking()
                .Where(a => adjustmentIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.SessionId, ct);

        var sessionIds = entities
            .Where(e => e.Type == nameof(Session) || e.Type == nameof(PointAdjustment))
            .Select(e => e.Id)
            .Concat(roundSession.Values)
            .Concat(adjustmentSession.Values)
            .Distinct()
            .ToList();

        var sessions = sessionIds.Count == 0
            ? new Dictionary<Guid, ActivitySessionDto>()
            : await db.Sessions.AsNoTracking()
                .Where(s => sessionIds.Contains(s.Id))
                .Select(s => new ActivitySessionDto(s.Id, s.Division.Name, s.Date))
                .ToDictionaryAsync(s => s.Id, ct);

        return (type, id) =>
        {
            Guid? sessionId = type switch
            {
                nameof(Session) => id,
                nameof(Round) => roundSession.TryGetValue(id, out var s) ? s : null,
                nameof(PointAdjustment) => adjustmentSession.TryGetValue(id, out var s) ? s : id,
                _ => null,
            };

            return sessionId is { } key && sessions.TryGetValue(key, out var session) ? session : null;
        };
    }

    /// <summary>
    /// Names for every id mentioned inside the entries' data.
    /// </summary>
    /// <remarks>
    /// Looked up by id alone, across games, teams and scoring rules, without
    /// needing to know which property of which action holds which kind of id.
    /// That keeps this working for actions added later without anyone
    /// remembering to teach it about them.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, string>> NamesForAsync(
        IEnumerable<JsonDocument?> data, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();

        foreach (var document in data)
        {
            if (document is not null) CollectIds(document.RootElement, ids);
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return names;

        var list = ids.ToList();

        foreach (var game in await db.Games.AsNoTracking()
                     .Where(g => list.Contains(g.Id)).Select(g => new { g.Id, g.Name })
                     .ToListAsync(ct))
        {
            names[game.Id.ToString()] = game.Name;
        }

        foreach (var team in await db.Teams.AsNoTracking()
                     .Where(t => list.Contains(t.Id)).Select(t => new { t.Id, t.Name })
                     .ToListAsync(ct))
        {
            names[team.Id.ToString()] = team.Name;
        }

        foreach (var profile in await db.ScoringProfiles.AsNoTracking()
                     .Where(p => list.Contains(p.Id)).Select(p => new { p.Id, p.Name })
                     .ToListAsync(ct))
        {
            names[profile.Id.ToString()] = profile.Name;
        }

        return names;
    }

    private static void CollectIds(JsonElement element, HashSet<Guid> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectIds(property.Value, into);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectIds(item, into);
                break;
            case JsonValueKind.String when Guid.TryParse(element.GetString(), out var id) && id != Guid.Empty:
                into.Add(id);
                break;
        }
    }

    // -------------------------------------------------------------- helpers

    private Task<AppUser?> Find(Guid churchId, Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id && u.ChurchId == churchId, ct);

    private static PersonDto ToDto(AppUser user, IReadOnlySet<string> configAdmins) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role.ToString(),
        user.IsActive,
        user.LastLoginAt is not null,
        user.LastLoginAt,
        user.CreatedAt,
        configAdmins.Contains(user.Email));

    /// <summary>
    /// By name only. Enum.TryParse would also accept "40" and "Admin, Viewer",
    /// and a role granted by a number somebody typed is not one to hand out.
    /// </summary>
    private static UserRole? ParseRole(string? value) => value switch
    {
        nameof(UserRole.Viewer) => UserRole.Viewer,
        nameof(UserRole.Scorekeeper) => UserRole.Scorekeeper,
        nameof(UserRole.GamesLeader) => UserRole.GamesLeader,
        nameof(UserRole.Admin) => UserRole.Admin,
        _ => null,
    };

    /// <summary>
    /// Lowercased and trimmed, the way the seeder and the sign-in lookup both
    /// store and compare addresses. Null when it is not an address at all.
    /// </summary>
    private static string? NormalizeEmail(string? raw)
    {
        var email = raw?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(email) || email.Length > MaxEmail) return null;

        // MailAddress accepts "Name <a@b>" and quietly returns the inner part,
        // so the round trip is what confirms this was only an address.
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && email.Contains('.')
            ? email
            : null;
    }

    private void AddAudit(Guid churchId, Guid? actorId, string action, Guid entityId, object data) =>
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = churchId,
            ActorUserId = actorId,
            Action = action,
            EntityType = nameof(AppUser),
            EntityId = entityId,
            Data = JsonSerializer.SerializeToDocument(data),
        });
}
