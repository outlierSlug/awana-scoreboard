using Awana.Api.Contracts;
using Awana.Data;
using Awana.Data.Entities;
using Awana.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Services;

/// <summary>
/// Builds the scoreboard from stored rounds.
///
/// Nothing is cached. Every call re-reads the rounds and re-adds them, which is
/// affordable at thirty-two rows and means the board can never show a total
/// that disagrees with the rounds behind it.
/// </summary>
public class ScoreboardService(AwanaDbContext db)
{
    public async Task<ScoreboardDto?> BuildAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        return session is null ? null : await BuildAsync(session, ct);
    }

    public async Task<ScoreboardDto?> BuildBySlugAsync(string slug, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Division)
            .FirstOrDefaultAsync(s => s.PublicSlug == slug, ct);

        return session is null ? null : await BuildAsync(session, ct);
    }

    private async Task<ScoreboardDto> BuildAsync(Session session, CancellationToken ct)
    {
        var teams = await db.Teams
            .AsNoTracking()
            .Where(t => t.DivisionId == session.DivisionId && t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

        // Voided rounds are excluded here rather than subtracted later. There is
        // no total to correct, so a void simply stops contributing.
        var rounds = await db.Rounds
            .AsNoTracking()
            .Where(r => r.SessionId == session.Id && r.VoidedAt == null)
            .Include(r => r.Game)
            .Include(r => r.Results)
            .OrderBy(r => r.RoundNumber)
            .ToListAsync(ct);

        var adjustments = await db.PointAdjustments
            .AsNoTracking()
            .Where(a => a.SessionId == session.Id && a.VoidedAt == null)
            .GroupBy(a => a.TeamId)
            .Select(g => new { TeamId = g.Key, Points = g.Sum(a => a.Points) })
            .ToDictionaryAsync(x => x.TeamId, x => x.Points, ct);

        var tallies = rounds
            .Select(r => new RoundTally(
                r.RoundNumber,
                r.Results
                    .Select(res => new RoundEntry(
                        res.TeamId, res.Place, res.PointsAwarded + res.BonusPoints))
                    .ToList()))
            .ToList();

        var standings = StandingsCalculator.Compute(
            teams.Select(t => t.Id).ToList(), tallies, adjustments);

        var byId = teams.ToDictionary(t => t.Id);

        var lastRound = rounds.Count == 0 ? null : ToLastRound(rounds[^1], byId);

        return new ScoreboardDto(
            SessionId: session.Id,
            Slug: session.PublicSlug,
            DivisionName: session.Division.Name,
            Date: session.Date,
            Status: session.Status,
            Version: session.Version,
            RoundCount: rounds.Count,
            Standings: standings
                .Select(s => new StandingDto(
                    s.TeamId,
                    byId[s.TeamId].Name,
                    byId[s.TeamId].ColorHex,
                    byId[s.TeamId].TextOnColorHex,
                    s.Points,
                    s.Rank,
                    s.RankChange,
                    s.PlaceCounts))
                .ToList(),
            LastRound: lastRound,
            ServerTimeUtc: DateTimeOffset.UtcNow);
    }

    internal static LastRoundDto ToLastRound(Round round, IReadOnlyDictionary<Guid, Team> teams) =>
        new(
            round.Id,
            round.RoundNumber,
            round.Game.Name,
            round.PointMultiplier,
            round.Results
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
                .ToList());
}
