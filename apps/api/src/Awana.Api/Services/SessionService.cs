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
    /// <summary>
    /// Every session, with whatever is live at the top.
    /// </summary>
    /// <remarks>
    /// Live first, then newest date first. Date alone is not enough: a session
    /// created for next month sorts above the one running tonight, and tonight's
    /// is the whole reason anybody has this page open on a Friday.
    /// </remarks>
    public async Task<IReadOnlyList<SessionSummaryDto>> ListAsync(CancellationToken ct = default) =>
        await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .OrderByDescending(s => s.Status == SessionStatus.Running)
            .ThenByDescending(s => s.Date)
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
            // Normally all of one night, where the division order is what shows.
            // Dated first anyway, so a session left running from a past week
            // cannot sit above tonight's.
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

        var scoring = await DescribeScoringAsync(session, ct);

        return ServiceResult<SessionDetailDto>.Success(new SessionDetailDto(
            session.Id,
            session.PublicSlug,
            session.DivisionId,
            session.Division.Name,
            session.Date,
            session.Status,
            session.Version,
            scoring,
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

    /// <summary>
    /// What this session scores under, before or after it has started.
    /// </summary>
    /// <remarks>
    /// After starting, the numbers come from the session's own frozen copy,
    /// which is the only honest source: the profile it was taken from may have
    /// been edited since. The NAME still comes from the profile, so a profile
    /// renamed later reads by its current name, the same way a renamed game
    /// does throughout the history.
    /// </remarks>
    private async Task<SessionScoringDto> DescribeScoringAsync(Session session, CancellationToken ct)
    {
        var chosen = session.ScoringProfileId is { } id
            ? await db.ScoringProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            : null;

        // Nothing chosen means the default, which is also what StartAsync will
        // reach for. Resolved the same way here so the screen does not promise
        // something different from what will happen.
        var fallback = chosen is null
            ? await db.ScoringProfiles
                .AsNoTracking()
                .Where(p => p.ChurchId == session.ChurchId && p.IsActive)
                .OrderByDescending(p => p.IsDefault)
                .FirstOrDefaultAsync(ct)
            : null;

        var profile = chosen ?? fallback;
        var isFixed = session.ScoringConfig is not null;

        return new SessionScoringDto(
            profile?.Name ?? "Built-in default",
            session.ScoringConfig?.PlacePoints ?? profile?.Config.PlacePoints ?? ScoringConfig.Default.PlacePoints,
            isFixed,
            chosen is null);
    }

    public async Task<ServiceResult<SessionDetailDto>> CreateAsync(
        CreateSessionRequest request, Guid? userId, CancellationToken ct = default)
    {
        var division = await db.Divisions.FirstOrDefaultAsync(d => d.Id == request.DivisionId, ct);
        if (division is null) return ServiceResult<SessionDetailDto>.Fail(ServiceError.NotFound("Division"));

        // Chosen now, frozen at start. Recording the choice here rather than
        // resolving it immediately is what lets a session be made in advance
        // and still pick up a correction to the rules before the night begins.
        if (request.ScoringProfileId is { } profileId)
        {
            var usable = await db.ScoringProfiles.AnyAsync(
                p => p.Id == profileId && p.ChurchId == division.ChurchId && p.IsActive, ct);

            if (!usable)
            {
                return ServiceResult<SessionDetailDto>.Fail(
                    ServiceError.Invalid("Those scoring rules do not exist, or have been retired."));
            }
        }

        var session = new Session
        {
            ChurchId = division.ChurchId,
            DivisionId = division.Id,
            Date = request.Date,
            PublicSlug = await UniqueSlugAsync(division.Slug, request.Date, ct),
            Status = SessionStatus.Setup,
            ScoringProfileId = request.ScoringProfileId,
            CreatedByUserId = userId,
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        return await GetAsync(session.Id, ct);
    }

    /// <summary>
    /// Changes which rules a session will run under, before it has started.
    /// </summary>
    /// <remarks>
    /// Only in setup, and that is the whole point of the guard rather than
    /// caution: a running session has already frozen its copy of the rules, so
    /// moving the pointer afterwards would leave it naming one set of rules and
    /// scoring by another. A night made a week in advance, though, has decided
    /// nothing yet, and the choice made at creation should not be final.
    /// </remarks>
    public async Task<ServiceResult<SessionDetailDto>> SetScoringAsync(
        Guid id, Guid? scoringProfileId, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return ServiceResult<SessionDetailDto>.Fail(ServiceError.NotFound("Session"));

        if (session.Status != SessionStatus.Setup)
        {
            return ServiceResult<SessionDetailDto>.Fail(ServiceError.Conflict(
                "session_already_started",
                "This session has already started, so its scoring rules are fixed. They cannot be changed now."));
        }

        if (scoringProfileId is { } profileId)
        {
            var usable = await db.ScoringProfiles.AnyAsync(
                p => p.Id == profileId && p.ChurchId == session.ChurchId && p.IsActive, ct);

            if (!usable)
            {
                return ServiceResult<SessionDetailDto>.Fail(
                    ServiceError.Invalid("Those scoring rules do not exist, or have been retired."));
            }
        }

        var previous = session.ScoringProfileId;
        session.ScoringProfileId = scoringProfileId;

        AddAudit(session, userId, "session.scoring_set", nameof(Session), session.Id, new
        {
            From = previous,
            ScoringProfileId = scoringProfileId,
        });

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

        // The set chosen when the session was made, if one was, and otherwise
        // the church's default. A profile retired between creating the session
        // and starting it is still honored: the choice was made deliberately
        // and the night should run under the rules it was set up with.
        var profile = session.ScoringProfileId is { } chosen
            ? await db.ScoringProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == chosen, ct)
            : await db.ScoringProfiles
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

        // The rules are frozen here, so this is the entry that can answer "what
        // was that night scored under" long after the set itself was edited.
        AddAudit(session, userId, "session.started", nameof(Session), session.Id, new
        {
            session.Status,
            session.Version,
            Scoring = profile?.Name,
            session.ScoringConfig.PlacePoints,
        });
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


    // --------------------------------------------------------------- delete

    /// <summary>
    /// Removes a session that never happened.
    /// </summary>
    /// <remarks>
    /// Only while nothing stands against it. Session to round is a cascade, so
    /// deleting one that has been played would erase the night rather than
    /// correct it, and everything else here is retired or voided instead of
    /// deleted for exactly that reason.
    ///
    /// "Stands" means what it means everywhere else in the product: the round
    /// count on the list, the board and the session summary all leave out
    /// cleared rounds. Counting them only here made the rule invisible to the
    /// person it was being applied to, who saw a session with no rounds and was
    /// told it had rounds. A session whose rounds were all cleared can go, and
    /// the audit log keeps what was done to it either way.
    /// </remarks>
    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid id, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .Include(s => s.Rounds)
            .Include(s => s.PointAdjustments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null) return ServiceResult<bool>.Fail(ServiceError.NotFound("Session"));

        var standingRounds = session.Rounds.Count(r => r.VoidedAt is null);
        var standingAdjustments = session.PointAdjustments.Count(a => a.VoidedAt is null);

        if (standingRounds > 0 || standingAdjustments > 0)
        {
            return ServiceResult<bool>.Fail(ServiceError.Conflict(
                "session_not_empty",
                standingRounds > 0
                    ? $"This session still has {standingRounds} {(standingRounds == 1 ? "round" : "rounds")} standing. Clear them first, or leave the session as the record of what happened."
                    : "This session still has points adjusted on it. Clear those first, or leave the session as the record of what happened."));
        }

        // Audited before the delete, because the row it points at is about to
        // stop existing and the log is the only place it will be mentioned.
        var divisionName = await db.Divisions
            .Where(d => d.Id == session.DivisionId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct);

        AddAudit(session, userId, "session.deleted", nameof(Session), session.Id, new
        {
            // Without this the entry could only say "a session on the 14th",
            // and there are usually two of those.
            DivisionName = divisionName,
            session.PublicSlug,
            session.Date,
            session.Status,
            // Named here because the rows themselves are about to go with the
            // session, and this entry becomes the only mention of them.
            ClearedRounds = session.Rounds.Count,
            ClearedAdjustments = session.PointAdjustments.Count,
        });

        db.Sessions.Remove(session);
        await db.SaveChangesAsync(ct);

        return ServiceResult<bool>.Success(true);
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
            nameof(PointAdjustment), adjustment.Id, new { adjustment.TeamId, adjustment.Points, adjustment.Reason });

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
        var changes = new List<object>();

        foreach (var entry in request.Teams)
        {
            if (byTeam.TryGetValue(entry.TeamId, out var sessionTeam) && sessionTeam.Headcount != entry.Headcount)
            {
                changes.Add(new { entry.TeamId, From = sessionTeam.Headcount, To = entry.Headcount });
                sessionTeam.Headcount = entry.Headcount;
            }
        }

        // A save that changes nothing records nothing, so every headcount line
        // in the log is a number that actually moved.
        if (changes.Count == 0) return await GetAsync(sessionId, ct);

        AddAudit(session, userId, "attendance.updated", nameof(Session), session.Id, new
        {
            Recorded = request.Teams.Count(t => t.Headcount is not null),
            Teams = changes,
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
            Data = AuditData.ForSession(session.Id, data),
        });

    private void AddAudit(Session session, Guid? userId, string action) =>
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = session.ChurchId,
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(Session),
            EntityId = session.Id,
            Data = AuditData.ForSession(session.Id, new { session.Status, session.Version }),
        });
}
