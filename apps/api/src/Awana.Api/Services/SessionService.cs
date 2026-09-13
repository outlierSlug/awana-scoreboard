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

    public async Task<ServiceResult<SessionDetailDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .Include(s => s.SessionTeams)
            .Include(s => s.Rounds).ThenInclude(r => r.Game)
            .Include(s => s.Rounds).ThenInclude(r => r.Results)
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
