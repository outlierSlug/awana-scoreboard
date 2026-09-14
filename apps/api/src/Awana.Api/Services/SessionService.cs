using System.Text.Json;
using Awana.Api.Contracts;
using Awana.Api.Realtime;
using Awana.Data;
using Awana.Data.Entities;
using Awana.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Services;

public class SessionService(
    AwanaDbContext db,
    ScoreboardService scoreboard,
    IScoreboardBroadcaster broadcaster)
{
    public async Task<IReadOnlyList<SessionSummaryDto>> ListAsync(CancellationToken ct = default) =>
        await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .OrderByDescending(s => s.Date)
            .ThenBy(s => s.Division.SortOrder)
            .Select(s => new SessionSummaryDto(
                s.Id,
                s.PublicSlug,
                s.Division.Name,
                s.Date,
                s.Status,
                s.Rounds.Count(r => r.VoidedAt == null)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SessionSummaryDto>> ListLiveAsync(
        string churchSlug, CancellationToken ct = default) =>
        await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .Where(s => s.Church.Slug == churchSlug && s.Status == SessionStatus.Running)
            .OrderBy(s => s.Division.SortOrder)
            .Select(s => new SessionSummaryDto(
                s.Id,
                s.PublicSlug,
                s.Division.Name,
                s.Date,
                s.Status,
                s.Rounds.Count(r => r.VoidedAt == null)))
            .ToListAsync(ct);

    /// <summary>
    /// Nights that are over, newest first.
    /// </summary>
    /// <remarks>
    /// Capped rather than paged. Somebody looking for last week's result wants
    /// the last few weeks, and a club accumulates one of these a week, so a
    /// page control here would be scaffolding for a list nobody scrolls.
    /// </remarks>
    public async Task<IReadOnlyList<SessionSummaryDto>> ListFinishedAsync(
        string churchSlug, int limit = 12, CancellationToken ct = default) =>
        await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .Where(s => s.Church.Slug == churchSlug && s.Status == SessionStatus.Finished)
            .OrderByDescending(s => s.Date)
            .ThenBy(s => s.Division.SortOrder)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(s => new SessionSummaryDto(
                s.Id,
                s.PublicSlug,
                s.Division.Name,
                s.Date,
                s.Status,
                s.Rounds.Count(r => r.VoidedAt == null)))
            .ToListAsync(ct);

    public async Task<ServiceResult<SessionDetailDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .Include(s => s.SessionTeams)
            .Include(s => s.Rounds).ThenInclude(r => r.Game)
            .Include(s => s.Rounds).ThenInclude(r => r.Results)
            .Include(s => s.PointAdjustments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null) return ServiceResult<SessionDetailDto>.Fail(ServiceError.NotFound("Session"));

        var teams = await db.Teams.AsNoTracking()
            .Where(t => t.DivisionId == session.DivisionId && t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

        var headcounts = session.SessionTeams.ToDictionary(st => st.TeamId, st => st.Headcount);
        var byId = teams.ToDictionary(t => t.Id);

        return ServiceResult<SessionDetailDto>.Success(new SessionDetailDto(
            session.Id,
            session.PublicSlug,
            session.DivisionId,
            session.Division.Name,
            session.Date,
            session.Status,
            session.Version,
            teams.Select(t => new SessionTeamDto(
                t.Id, t.Name, t.ColorHex, t.TextOnColorHex,
                headcounts.GetValueOrDefault(t.Id))).ToList(),
            // Voided rounds are included here on purpose. The console's history
            // shows them struck through, which is how a scorekeeper confirms a
            // mistake was dealt with rather than wondering where it went.
            session.Rounds
                .OrderBy(r => r.RoundNumber)
                .Select(r => new RoundSummaryDto(
                    r.Id,
                    r.RoundNumber,
                    r.GameId,
                    r.Game.Name,
                    r.PointMultiplier,
                    r.VoidedAt is not null,
                    r.VoidReason,
                    r.Results
                        .OrderBy(res => res.Place ?? int.MaxValue)
                        .ThenBy(res => res.TeamId)
                        .Select(res => new RoundTeamDto(
                            res.TeamId,
                            byId.TryGetValue(res.TeamId, out var t) ? t.Name : "Unknown",
                            res.Place,
                            res.IsDisqualified,
                            res.PointsAwarded + res.BonusPoints,
                            res.BonusPoints,
                            res.Explanation))
                        .ToList()))
                .ToList(),
            // Voided ones are kept for the same reason voided rounds are: the
            // night has to remain explainable afterwards.
            session.PointAdjustments
                .OrderBy(a => a.CreatedAt)
                .Select(a => new AdjustmentDto(
                    a.Id,
                    a.TeamId,
                    byId.TryGetValue(a.TeamId, out var at) ? at.Name : "Unknown",
                    a.Points,
                    a.Reason,
                    a.VoidedAt is not null,
                    a.CreatedAt))
                .ToList()));
    }

    public async Task<ServiceResult<SessionDetailDto>> CreateAsync(
        CreateSessionRequest request, Guid? userId, CancellationToken ct = default)
    {
        var division = await db.Divisions.FirstOrDefaultAsync(d => d.Id == request.DivisionId, ct);
        if (division is null) return ServiceResult<SessionDetailDto>.Fail(ServiceError.NotFound("Division"));

        var session = new Session
        {
            ChurchId = division.ChurchId,
            DivisionId = division.Id,
            Date = request.Date,
            PublicSlug = await UniqueSlugAsync(division.Slug, request.Date, ct),
            Status = SessionStatus.Setup,
            CreatedByUserId = userId,
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        return await GetAsync(session.Id, ct);
    }

    /// <summary>
    /// Setup to Running. This is the moment the scoring rules are frozen onto
    /// the session, which is what stops a later profile edit from rewriting
    /// tonight's results.
    /// </summary>
    public async Task<ServiceResult<ScoreboardDto>> StartAsync(
        Guid id, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return ServiceResult<ScoreboardDto>.Fail(ServiceError.NotFound("Session"));

        if (session.Status != SessionStatus.Setup)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Conflict(
                "session_already_started", "Only a session in setup can be started."));
        }

        var profile = await db.ScoringProfiles
            .AsNoTracking()
            .Where(p => p.ChurchId == session.ChurchId && p.IsActive)
            .OrderByDescending(p => p.IsDefault)
            .FirstOrDefaultAsync(ct);

        session.ScoringProfileId = profile?.Id;
        session.ScoringConfig = profile?.Config ?? ScoringConfig.Default;
        session.ScoringConfigSchemaVersion = profile?.SchemaVersion ?? 1;
        session.Status = SessionStatus.Running;
        session.StartedAt = DateTimeOffset.UtcNow;
        session.Version++;

        // Every active team in the division takes part. Headcounts stay null
        // until somebody chooses to record them.
        var existing = await db.SessionTeams
            .Where(st => st.SessionId == session.Id)
            .Select(st => st.TeamId)
            .ToListAsync(ct);

        var teams = await db.Teams
            .Where(t => t.DivisionId == session.DivisionId && t.IsActive)
            .ToListAsync(ct);

        foreach (var team in teams.Where(t => !existing.Contains(t.Id)))
        {
            db.SessionTeams.Add(new SessionTeam { SessionId = session.Id, TeamId = team.Id });
        }

        AddAudit(session, userId, "session.started");
        await db.SaveChangesAsync(ct);

        return await BroadcastStatusAsync(session.Id, ct);
    }

    public async Task<ServiceResult<ScoreboardDto>> FinishAsync(
        Guid id, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return ServiceResult<ScoreboardDto>.Fail(ServiceError.NotFound("Session"));

        if (session.Status != SessionStatus.Running)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Conflict(
                "session_not_running", "Only a running session can be finished."));
        }

        session.Status = SessionStatus.Finished;
        session.FinishedAt = DateTimeOffset.UtcNow;
        session.Version++;

        AddAudit(session, userId, "session.finished");
        await db.SaveChangesAsync(ct);

        return await BroadcastStatusAsync(session.Id, ct);
    }

    /// <summary>
    /// Finished back to Running. You will want this in the gym, when someone
    /// finishes the session and then remembers there was one more game.
    /// </summary>
    public async Task<ServiceResult<ScoreboardDto>> ReopenAsync(
        Guid id, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return ServiceResult<ScoreboardDto>.Fail(ServiceError.NotFound("Session"));

        if (session.Status != SessionStatus.Finished)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Conflict(
                "session_not_finished", "Only a finished session can be reopened."));
        }

        session.Status = SessionStatus.Running;
        session.FinishedAt = null;
        session.Version++;

        AddAudit(session, userId, "session.reopened");
        await db.SaveChangesAsync(ct);

        return await BroadcastStatusAsync(session.Id, ct);
    }


    // -------------------------------------------------------- adjustments

    /// <summary>Matches the column, so a long reason is a 400 and not a 500.</summary>
    private const int MaxReason = 500;

    /// <summary>A ceiling for the same reason the engine has one.</summary>
    private const decimal MaxAdjustment = 10_000m;

    /// <summary>
    /// Points a leader decided on, outside the games.
    /// </summary>
    /// <remarks>
    /// Always carries a written reason. An unexplained adjustment is
    /// indistinguishable from a bug, and the whole value of keeping these apart
    /// from round results is that a total stays explainable afterwards.
    /// </remarks>
    public async Task<ServiceResult<ScoreboardDto>> AddAdjustmentAsync(
        Guid sessionId, CreateAdjustmentRequest request, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .Include(s => s.SessionTeams)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null) return ServiceResult<ScoreboardDto>.Fail(ServiceError.NotFound("Session"));

        if (session.Status == SessionStatus.Setup)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Conflict(
                "session_not_running", "Start the session before adjusting points."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > MaxReason)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Invalid(
                $"Say why in 1 to {MaxReason} characters. The reason is the only record of what this was for."));
        }

        if (request.Points == 0m || Math.Abs(request.Points) > MaxAdjustment)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Invalid(
                $"An adjustment has to be between -{MaxAdjustment} and {MaxAdjustment}, and not zero."));
        }

        if (session.SessionTeams.All(st => st.TeamId != request.TeamId))
        {
            return ServiceResult<ScoreboardDto>.Fail(
                ServiceError.Invalid("That team is not taking part in this session."));
        }

        db.PointAdjustments.Add(new PointAdjustment
        {
            ChurchId = session.ChurchId,
            SessionId = session.Id,
            TeamId = request.TeamId,
            Points = request.Points,
            Reason = request.Reason.Trim(),
            CreatedByUserId = userId,
        });

        session.Version++;
        AddAudit(session, userId, "adjustment.added", nameof(PointAdjustment), session.Id, new
        {
            request.TeamId,
            request.Points,
            Reason = request.Reason.Trim(),
        });

        await db.SaveChangesAsync(ct);

        return await BroadcastBoardAsync(session.Id, ct);
    }

    /// <summary>Voided rather than deleted, like a round.</summary>
    public async Task<ServiceResult<ScoreboardDto>> VoidAdjustmentAsync(
        Guid adjustmentId, Guid? userId, CancellationToken ct = default)
    {
        var adjustment = await db.PointAdjustments
            .Include(a => a.Session)
            .FirstOrDefaultAsync(a => a.Id == adjustmentId, ct);

        if (adjustment is null)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.NotFound("Adjustment"));
        }

        if (adjustment.VoidedAt is not null)
        {
            return ServiceResult<ScoreboardDto>.Fail(
                ServiceError.Conflict("adjustment_voided", "This adjustment is already cleared."));
        }

        if (adjustment.Session.Status != SessionStatus.Running)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Conflict(
                "session_not_running",
                "This session is not running. Reopen it before changing an adjustment."));
        }

        adjustment.VoidedAt = DateTimeOffset.UtcNow;
        adjustment.VoidedByUserId = userId;

        adjustment.Session.Version++;
        AddAudit(adjustment.Session, userId, "adjustment.voided",
            nameof(PointAdjustment), adjustment.Id, new { adjustment.TeamId, adjustment.Points });

        await db.SaveChangesAsync(ct);

        return await BroadcastBoardAsync(adjustment.SessionId, ct);
    }

    // --------------------------------------------------------- attendance

    /// <summary>
    /// How many turned up, per team. Entirely optional.
    /// </summary>
    /// <remarks>
    /// A number and nothing else. This is a scoreboard, not an attendance
    /// system: the database never holds a child's name, and a session with no
    /// headcounts recorded is completely valid.
    /// </remarks>
    public async Task<ServiceResult<SessionDetailDto>> UpdateAttendanceAsync(
        Guid sessionId, UpdateAttendanceRequest request, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .Include(s => s.SessionTeams)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null) return ServiceResult<SessionDetailDto>.Fail(ServiceError.NotFound("Session"));

        if (request.Teams.Any(t => t.Headcount is < 0 or > 1000))
        {
            return ServiceResult<SessionDetailDto>.Fail(
                ServiceError.Invalid("A headcount has to be between 0 and 1000, or blank."));
        }

        var byTeam = session.SessionTeams.ToDictionary(st => st.TeamId);

        foreach (var entry in request.Teams)
        {
            if (byTeam.TryGetValue(entry.TeamId, out var sessionTeam))
            {
                sessionTeam.Headcount = entry.Headcount;
            }
        }

        AddAudit(session, userId, "attendance.updated", nameof(Session), session.Id, new
        {
            Recorded = request.Teams.Count(t => t.Headcount is not null),
        });

        await db.SaveChangesAsync(ct);

        return await GetAsync(sessionId, ct);
    }

    private async Task<ServiceResult<ScoreboardDto>> BroadcastBoardAsync(Guid id, CancellationToken ct)
    {
        var board = await scoreboard.BuildAsync(id, ct);
        if (board is not null) await broadcaster.ScoreboardUpdatedAsync(board, ct);
        return ServiceResult<ScoreboardDto>.Success(board!);
    }

    private async Task<ServiceResult<ScoreboardDto>> BroadcastStatusAsync(Guid id, CancellationToken ct)
    {
        var board = await scoreboard.BuildAsync(id, ct)!;
        if (board is not null) await broadcaster.SessionStatusChangedAsync(board, ct);
        return ServiceResult<ScoreboardDto>.Success(board!);
    }

    /// <summary>
    /// "tnt-2026-10-02", with a suffix if that is somehow taken. Two sessions
    /// for one division on one night is unusual but not worth refusing.
    /// </summary>
    private async Task<string> UniqueSlugAsync(string divisionSlug, DateOnly date, CancellationToken ct)
    {
        var basis = $"{divisionSlug}-{date:yyyy-MM-dd}";
        var candidate = basis;
        var suffix = 1;

        while (await db.Sessions.AnyAsync(s => s.PublicSlug == candidate, ct))
        {
            suffix++;
            candidate = $"{basis}-{suffix}";
        }

        return candidate;
    }

    private void AddAudit(
        Session session, Guid? userId, string action, string entityType, Guid entityId, object data) =>
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = session.ChurchId,
            ActorUserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = JsonSerializer.SerializeToDocument(data),
        });

    private void AddAudit(Session session, Guid? userId, string action) =>
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = session.ChurchId,
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(Session),
            EntityId = session.Id,
            Data = JsonSerializer.SerializeToDocument(new { session.Status, session.Version }),
        });
}
