using System.Text.Json;
using Awana.Api.Contracts;
using Awana.Api.Realtime;
using Awana.Data;
using Awana.Data.Entities;
using Awana.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Services;

/// <summary>
/// Recording, editing and voiding rounds.
///
/// Every path that changes a score goes through here, so the ordering rules,
/// the idempotency check and the version bump exist once rather than per
/// endpoint.
/// </summary>
public class RoundService(
    AwanaDbContext db,
    ScoreboardService scoreboard,
    IScoreboardBroadcaster broadcaster,
    IScoringEngine engine)
{
    /// <summary>Matches the column, so a long reason is a 400 and not a 500.</summary>
    private const int MaxVoidReason = 500;

    // ------------------------------------------------------------- preview

    /// <summary>
    /// What this round would score, writing nothing. The console calls this on
    /// every change so the scorekeeper confirms points rather than just an order.
    /// </summary>
    public async Task<ServiceResult<PreviewDto>> PreviewAsync(
        Guid sessionId, PreviewRequest request, CancellationToken ct = default)
    {
        var session = await db.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null) return ServiceResult<PreviewDto>.Fail(ServiceError.NotFound("Session"));

        var config = await ResolveConfigAsync(session, ct);
        var teams = await TeamsForAsync(session, ct);

        var unknown = request.Entries.Where(e => !teams.ContainsKey(e.TeamId)).ToList();
        if (unknown.Count > 0)
        {
            return ServiceResult<PreviewDto>.Fail(
                ServiceError.Invalid("A team in this round does not belong to the session's division."));
        }

        var input = ToRoundInput(request.Entries, request.Multiplier, teams);
        var validation = engine.Validate(input, config);

        if (!validation.IsValid)
        {
            return ServiceResult<PreviewDto>.Success(new PreviewDto(
                IsValid: false,
                Errors: validation.Errors.Select(e => new ValidationErrorDto(e.Code, e.Message)).ToList(),
                Awards: []));
        }

        var outcome = engine.Score(input, config);

        return ServiceResult<PreviewDto>.Success(new PreviewDto(
            IsValid: true,
            Errors: [],
            Awards: ToAwardDtos(outcome, request.Entries, teams)));
    }

    // -------------------------------------------------------------- create

    /// <param name="wasReplay">
    /// True when this request had already been recorded and the stored round is
    /// being returned instead of a new one. The endpoint answers 200 rather
    /// than 201 so a client can tell the difference.
    /// </param>
    public async Task<ServiceResult<(RoundRecordedDto Dto, bool WasReplay)>> CreateAsync(
        Guid sessionId, CreateRoundRequest request, Guid? userId, CancellationToken ct = default)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null)
        {
            return ServiceResult<(RoundRecordedDto, bool)>.Fail(ServiceError.NotFound("Session"));
        }

        if (session.Status != SessionStatus.Running)
        {
            return ServiceResult<(RoundRecordedDto, bool)>.Fail(ServiceError.Conflict(
                "session_not_running",
                $"This session is {session.Status.ToString().ToLowerInvariant()}, so rounds cannot be recorded."));
        }

        // The retry case, checked before doing any work.
        var replay = await FindByRequestIdAsync(sessionId, request.ClientRequestId, ct);
        if (replay is not null)
        {
            return ServiceResult<(RoundRecordedDto, bool)>.Success((await ToRecordedAsync(replay, ct), true));
        }

        var config = await ResolveConfigAsync(session, ct);
        var teams = await TeamsForAsync(session, ct);

        if (request.Entries.Any(e => !teams.ContainsKey(e.TeamId)))
        {
            return ServiceResult<(RoundRecordedDto, bool)>.Fail(
                ServiceError.Invalid("A team in this round does not belong to the session's division."));
        }

        if (!await GameIsPlayableAsync(session, request.GameId, ct))
        {
            return ServiceResult<(RoundRecordedDto, bool)>.Fail(
                ServiceError.NotFound("Game"));
        }

        var input = ToRoundInput(request.Entries, request.Multiplier, teams);
        var validation = engine.Validate(input, config);

        if (!validation.IsValid)
        {
            return ServiceResult<(RoundRecordedDto, bool)>.Fail(ServiceError.Invalid(
                "This round cannot be scored.",
                validation.Errors.Select(e => new ValidationErrorDto(e.Code, e.Message)).ToList()));
        }

        var outcome = engine.Score(input, config);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Serialize round numbering for this session. Without the lock, two
        // scorekeepers confirming at the same moment can both read the same
        // MAX and collide on the unique index.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM sessions WHERE id = {sessionId} FOR UPDATE", ct);

        // Numbering skips voided rounds, so voiding round 3 frees the number
        // for the rerun that replaces it.
        var nextNumber = await db.Rounds
            .Where(r => r.SessionId == sessionId && r.VoidedAt == null)
            .Select(r => (int?)r.RoundNumber)
            .MaxAsync(ct) ?? 0;

        var round = new Round
        {
            ChurchId = session.ChurchId,
            SessionId = session.Id,
            RoundNumber = nextNumber + 1,
            GameId = request.GameId,
            PointMultiplier = request.Multiplier,
            ClientRequestId = request.ClientRequestId,
            RecordedByUserId = userId,
        };

        AttachResults(round, outcome, request.Entries);

        db.Rounds.Add(round);
        session.Version++;

        AddAudit(session, userId, "round.recorded", nameof(Round), round.Id, new
        {
            round.RoundNumber,
            request.GameId,
            request.Multiplier,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);

            // Drop the round that lost, so the context is not still holding an
            // unsaved insert when the query below runs.
            db.Entry(round).State = EntityState.Detached;
            foreach (var result in round.Results) db.Entry(result).State = EntityState.Detached;

            // Two identical retries arrived at once and the unique index caught
            // the second. That is the index doing exactly its job, so return
            // whichever round won rather than surfacing an error.
            var raced = await FindByRequestIdAsync(sessionId, request.ClientRequestId, ct);
            if (raced is null) throw;

            return ServiceResult<(RoundRecordedDto, bool)>.Success((await ToRecordedAsync(raced, ct), true));
        }

        var dto = await ToRecordedAsync(round, ct);
        await broadcaster.ScoreboardUpdatedAsync(dto.Scoreboard, ct);

        return ServiceResult<(RoundRecordedDto, bool)>.Success((dto, false));
    }

    // -------------------------------------------------------------- update

    public async Task<ServiceResult<RoundRecordedDto>> UpdateAsync(
        Guid roundId, UpdateRoundRequest request, Guid? userId, CancellationToken ct = default)
    {
        // Deliberately without the results: an edit replaces every one of them,
        // and loading them only to delete them through the change tracker is
        // what made this fail. See the delete below.
        var round = await db.Rounds
            .Include(r => r.Session)
            .FirstOrDefaultAsync(r => r.Id == roundId, ct);

        if (round is null) return ServiceResult<RoundRecordedDto>.Fail(ServiceError.NotFound("Round"));

        if (round.VoidedAt is not null)
        {
            return ServiceResult<RoundRecordedDto>.Fail(
                ServiceError.Conflict("round_voided", "A voided round cannot be edited."));
        }

        // A finished night is a result, not a draft. Changing a round inside
        // one rewrites the standings the room was shown and then sent home
        // with, so it takes the deliberate step of reopening the session first,
        // which is an admin's call and leaves an audit entry of its own.
        if (round.Session.Status != SessionStatus.Running)
        {
            return ServiceResult<RoundRecordedDto>.Fail(ServiceError.Conflict(
                "session_not_running",
                $"This session is {round.Session.Status.ToString().ToLowerInvariant()}. Reopen it before changing a round."));
        }

        var session = round.Session;
        var config = await ResolveConfigAsync(session, ct);
        var teams = await TeamsForAsync(session, ct);

        if (request.Entries.Any(e => !teams.ContainsKey(e.TeamId)))
        {
            return ServiceResult<RoundRecordedDto>.Fail(
                ServiceError.Invalid("A team in this round does not belong to the session's division."));
        }

        if (!await GameIsPlayableAsync(session, request.GameId, ct))
        {
            return ServiceResult<RoundRecordedDto>.Fail(ServiceError.NotFound("Game"));
        }

        var input = ToRoundInput(request.Entries, request.Multiplier, teams);
        var validation = engine.Validate(input, config);

        if (!validation.IsValid)
        {
            return ServiceResult<RoundRecordedDto>.Fail(ServiceError.Invalid(
                "This round cannot be scored.",
                validation.Errors.Select(e => new ValidationErrorDto(e.Code, e.Message)).ToList()));
        }

        var outcome = engine.Score(input, config);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Clear the old results in one statement, outside the change tracker.
        //
        // Two reasons, both of which broke a tracked delete-then-insert here.
        // round_results is unique on (round_id, team_id), and one SaveChanges
        // does not promise to order deletes ahead of inserts for the same
        // table. And marking a loaded result Deleted makes EF remove it from
        // round.Results as it goes, so removing the range while enumerating
        // that same collection left rows behind. The transaction keeps the
        // round from being left with no results if the insert fails.
        await db.RoundResults.Where(r => r.RoundId == round.Id).ExecuteDeleteAsync(ct);

        round.GameId = request.GameId;
        round.PointMultiplier = request.Multiplier;

        db.RoundResults.AddRange(AttachResults(round, outcome, request.Entries));

        session.Version++;
        AddAudit(session, userId, "round.edited", nameof(Round), round.Id, new { round.RoundNumber });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var dto = await ToRecordedAsync(round, ct);
        await broadcaster.ScoreboardUpdatedAsync(dto.Scoreboard, ct);

        return ServiceResult<RoundRecordedDto>.Success(dto);
    }

    // ---------------------------------------------------------------- void

    public async Task<ServiceResult<ScoreboardDto>> VoidAsync(
        Guid roundId, string reason, Guid? userId, CancellationToken ct = default)
    {
        var round = await db.Rounds
            .Include(r => r.Session)
            .FirstOrDefaultAsync(r => r.Id == roundId, ct);

        if (round is null) return ServiceResult<ScoreboardDto>.Fail(ServiceError.NotFound("Round"));

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaxVoidReason)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Invalid(
                $"Say why in 1 to {MaxVoidReason} characters. The reason is the only record of what happened."));
        }

        if (round.VoidedAt is not null)
        {
            return ServiceResult<ScoreboardDto>.Fail(
                ServiceError.Conflict("round_voided", "This round is already voided."));
        }

        // A finished night is a result, not a draft. Changing a round inside
        // one rewrites the standings the room was shown and then sent home
        // with, so it takes the deliberate step of reopening the session first,
        // which is an admin's call and leaves an audit entry of its own.
        if (round.Session.Status != SessionStatus.Running)
        {
            return ServiceResult<ScoreboardDto>.Fail(ServiceError.Conflict(
                "session_not_running",
                $"This session is {round.Session.Status.ToString().ToLowerInvariant()}. Reopen it before changing a round."));
        }

        // Soft void. The row and its results stay, so the night can still be
        // explained, and standings simply stop counting it.
        round.VoidedAt = DateTimeOffset.UtcNow;
        round.VoidedByUserId = userId;
        round.VoidReason = reason;

        round.Session.Version++;
        AddAudit(round.Session, userId, "round.voided", nameof(Round), round.Id,
            new { round.RoundNumber, Reason = reason });

        await db.SaveChangesAsync(ct);

        var board = await scoreboard.BuildAsync(round.SessionId, ct);
        if (board is not null) await broadcaster.ScoreboardUpdatedAsync(board, ct);

        return ServiceResult<ScoreboardDto>.Success(board!);
    }

    // ------------------------------------------------------------- helpers


    /// <summary>
    /// The game exists and this division plays it.
    /// </summary>
    /// <remarks>
    /// Without this a bad id reached the insert and came back as a foreign key
    /// violation, which is a 500: a request that was wrong in an ordinary,
    /// explainable way reported as the server breaking.
    /// </remarks>
    private async Task<bool> GameIsPlayableAsync(Session session, Guid gameId, CancellationToken ct) =>
        await db.Games.AnyAsync(
            g => g.Id == gameId
                && g.ChurchId == session.ChurchId
                && (g.DivisionId == null || g.DivisionId == session.DivisionId),
            ct);

    private Task<Round?> FindByRequestIdAsync(Guid sessionId, Guid clientRequestId, CancellationToken ct) =>
        db.Rounds
            .Include(r => r.Results)
            .FirstOrDefaultAsync(r => r.SessionId == sessionId && r.ClientRequestId == clientRequestId, ct);

    /// <summary>
    /// A running session is scored against its own frozen copy of the rules.
    /// Before it starts there is no copy yet, so the church's default profile
    /// stands in, which is what lets the console preview during setup.
    /// </summary>
    private async Task<ScoringConfig> ResolveConfigAsync(Session session, CancellationToken ct)
    {
        if (session.ScoringConfig is not null) return session.ScoringConfig;

        var profile = await db.ScoringProfiles
            .AsNoTracking()
            .Where(p => p.ChurchId == session.ChurchId && p.IsActive)
            .OrderByDescending(p => p.IsDefault)
            .FirstOrDefaultAsync(ct);

        return profile?.Config ?? ScoringConfig.Default;
    }

    private Task<Dictionary<Guid, Team>> TeamsForAsync(Session session, CancellationToken ct) =>
        db.Teams
            .AsNoTracking()
            .Where(t => t.DivisionId == session.DivisionId && t.IsActive)
            .ToDictionaryAsync(t => t.Id, ct);

    private static RoundInput ToRoundInput(
        IReadOnlyList<RoundEntryRequest> entries, decimal multiplier, IReadOnlyDictionary<Guid, Team> teams) =>
        new(
            entries.Select(e => new TeamEntry(
                e.TeamId,
                e.Place,
                e.IsDisqualified,
                e.Bonus,
                teams.TryGetValue(e.TeamId, out var t) ? t.Name : null)).ToList(),
            multiplier);

    /// <returns>
    /// The results created, so a caller holding an already tracked round can
    /// add them to the set itself. Round ids are client generated, so EF reads
    /// a child appearing on a tracked parent's collection as an existing row to
    /// update rather than a new one to insert.
    /// </returns>
    private static List<RoundResult> AttachResults(
        Round round, ScoringOutcome outcome, IReadOnlyList<RoundEntryRequest> entries)
    {
        var byTeam = entries.ToDictionary(e => e.TeamId);
        var created = new List<RoundResult>(outcome.Awards.Count);

        foreach (var award in outcome.Awards)
        {
            var request = byTeam[award.TeamId];

            var result = new RoundResult
            {
                RoundId = round.Id,
                TeamId = award.TeamId,
                Place = award.Place,
                IsDisqualified = award.IsDisqualified,
                // The engine's total includes the bonus. Splitting it back out
                // keeps "what the placement was worth" and "what was awarded on
                // top" separately answerable, and the two still sum exactly.
                PointsAwarded = award.Points - request.Bonus,
                BonusPoints = request.Bonus,
                BonusReason = request.BonusReason,
                Explanation = award.Explanation,
            };

            round.Results.Add(result);
            created.Add(result);
        }

        return created;
    }

    private static IReadOnlyList<RoundTeamDto> ToAwardDtos(
        ScoringOutcome outcome,
        IReadOnlyList<RoundEntryRequest> entries,
        IReadOnlyDictionary<Guid, Team> teams)
    {
        var byTeam = entries.ToDictionary(e => e.TeamId);

        return outcome.Awards
            .Select(a => new RoundTeamDto(
                a.TeamId,
                teams.TryGetValue(a.TeamId, out var t) ? t.Name : "Unknown",
                a.Place,
                a.IsDisqualified,
                a.Points,
                byTeam[a.TeamId].Bonus,
                a.Explanation))
            .ToList();
    }

    private async Task<RoundRecordedDto> ToRecordedAsync(Round round, CancellationToken ct)
    {
        var board = await scoreboard.BuildAsync(round.SessionId, ct)
            ?? throw new InvalidOperationException("Session vanished while recording a round.");

        var teams = await db.Teams.AsNoTracking()
            .Where(t => t.ChurchId == round.ChurchId)
            .ToDictionaryAsync(t => t.Id, ct);

        var awards = round.Results
            .OrderBy(r => r.Place ?? int.MaxValue)
            .ThenBy(r => r.TeamId)
            .Select(r => new RoundTeamDto(
                r.TeamId,
                teams.TryGetValue(r.TeamId, out var t) ? t.Name : "Unknown",
                r.Place,
                r.IsDisqualified,
                r.PointsAwarded + r.BonusPoints,
                r.BonusPoints,
                r.Explanation))
            .ToList();

        return new RoundRecordedDto(round.Id, round.RoundNumber, awards, board);
    }

    private void AddAudit(Session session, Guid? userId, string action, string entityType, Guid entityId, object data) =>
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = session.ChurchId,
            ActorUserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = JsonSerializer.SerializeToDocument(data),
        });
}
