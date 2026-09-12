using System.Globalization;

namespace Awana.Scoring;

/// <summary>
/// Scores a round from recorded finishing places.
///
/// The whole algorithm is: work out which teams consume a finishing slot, split
/// the consumed slots between teams that finished level, then apply the round
/// multiplier and add bonuses. Everything else is configuration.
/// </summary>
public sealed class ScoringEngine : IScoringEngine
{
    public ValidationOutcome Validate(RoundInput input, ScoringConfig config)
    {
        var errors = new List<ValidationError>();

        if (config.PlacePoints.Count == 0)
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.EmptyPointsTable,
                "The scoring profile has no points table."));
        }

        if (config.Rounding.Decimals is < 0 or > 9)
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.InvalidRounding,
                "Rounding must be between 0 and 9 decimal places."));
        }

        if (input.Entries.Count == 0)
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.EmptyRound,
                "A round needs at least one team."));
        }

        if (input.Multiplier < 0m)
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.NegativeMultiplier,
                "The round multiplier cannot be negative."));
        }

        var duplicates = input.Entries
            .GroupBy(e => e.TeamId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.DuplicateTeam,
                $"A team appears more than once in this round ({duplicates.Count} affected)."));
        }

        if (input.Entries.Any(e => e.Place is <= 0))
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.InvalidPlace,
                "Finishing places start at 1."));
        }

        if (!config.AllowUnplacedTeams && input.Entries.Any(e => e.Place is null))
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.UnplacedNotAllowed,
                "Every team must be given a finishing place."));
        }

        if (config.TieRule == TieRule.NoTiesAllowed && HasTie(input))
        {
            errors.Add(new ValidationError(
                ScoringErrorCodes.TiesNotAllowed,
                "This scoring profile does not allow ties. Separate the tied teams."));
        }

        return new ValidationOutcome(errors);
    }

    public ScoringOutcome Score(RoundInput input, ScoringConfig config)
    {
        var validation = Validate(input, config);
        if (!validation.IsValid) throw new ScoringValidationException(validation);

        var awards = new List<TeamAward>(input.Entries.Count);

        // Teams that did not take part consume no slot and shift nobody.
        foreach (var entry in input.Entries.Where(e => e.Place is null))
        {
            var bonus = Round(entry.Bonus, config);
            awards.Add(new TeamAward(
                entry.TeamId,
                Place: null,
                Points: bonus,
                IsDisqualified: entry.IsDisqualified,
                Explanation: entry.Bonus == 0m
                    ? "Did not take part."
                    : $"Did not take part. Bonus {Format(bonus, config)}."));
        }

        var placed = input.Entries.Where(e => e.Place is not null).ToList();
        if (placed.Count == 0) return new ScoringOutcome(Order(awards));

        // The disqualification rule decides the ordering BEFORE any slot is
        // assigned, because some rules change who is standing where.
        var ordering = ApplyDqRule(placed, config);

        // Normalize to contiguous 1..n so that recording places as 1, 2, 4
        // scores identically to 1, 2, 3.
        var rankOf = ordering.SlotConsumers
            .Select(e => ordering.EffectivePlace[e.TeamId])
            .Distinct()
            .Order()
            .Select((place, index) => (place, rank: index + 1))
            .ToDictionary(x => x.place, x => x.rank);

        var groups = ordering.SlotConsumers
            .GroupBy(e => rankOf[ordering.EffectivePlace[e.TeamId]])
            .OrderBy(g => g.Key)
            .ToList();

        var slotCursor = 1;

        foreach (var group in groups)
        {
            var members = group.ToList();
            var slots = Enumerable.Range(slotCursor, members.Count).ToList();
            var slotValues = slots.Select(s => SlotValue(s, config)).ToList();
            var share = ShareFor(slotValues, config.TieRule);

            foreach (var entry in members)
            {
                awards.Add(BuildAward(entry, group.Key, slots, slotValues, share, input, config));
            }

            slotCursor += members.Count;
        }

        // Teams removed from the ordering by ZeroAndPromoteOthers still need a result.
        foreach (var entry in ordering.Excluded)
        {
            var bonus = Round(entry.Bonus, config);
            awards.Add(new TeamAward(
                entry.TeamId,
                entry.Place,
                Points: bonus,
                IsDisqualified: true,
                Explanation: $"Disqualified, {Format(bonus, config)}. Removed from the finishing order, so the teams behind moved up."));
        }

        return new ScoringOutcome(Order(awards));
    }

    // ---------------------------------------------------------------- helpers

    private static bool HasTie(RoundInput input) =>
        input.Entries
            .Where(e => e.Place is not null)
            .GroupBy(e => e.Place!.Value)
            .Any(g => g.Count() > 1);

    private sealed record Ordering(
        IReadOnlyList<TeamEntry> SlotConsumers,
        IReadOnlyDictionary<Guid, int> EffectivePlace,
        IReadOnlyList<TeamEntry> Excluded);

    private static Ordering ApplyDqRule(IReadOnlyList<TeamEntry> placed, ScoringConfig config)
    {
        switch (config.DqRule)
        {
            case DqRule.ZeroAndPromoteOthers:
            {
                var kept = placed.Where(e => !e.IsDisqualified).ToList();
                return new Ordering(
                    kept,
                    kept.ToDictionary(e => e.TeamId, e => e.Place!.Value),
                    placed.Where(e => e.IsDisqualified).ToList());
            }

            case DqRule.DropToLast:
            {
                // Park the disqualified behind everyone else, preserving their
                // relative order, then let normalization compact the numbers.
                var lastPlace = placed.Max(e => e.Place!.Value);
                var effective = new Dictionary<Guid, int>();

                foreach (var entry in placed)
                {
                    effective[entry.TeamId] = entry.IsDisqualified
                        ? lastPlace + entry.Place!.Value
                        : entry.Place!.Value;
                }

                return new Ordering(placed, effective, []);
            }

            default:
                // ZeroButHoldSlot and CustomPenalty both leave the ordering
                // untouched. The team keeps its slot and nobody is promoted.
                return new Ordering(
                    placed,
                    placed.ToDictionary(e => e.TeamId, e => e.Place!.Value),
                    []);
        }
    }

    private static decimal SlotValue(int slot, ScoringConfig config) =>
        slot <= config.PlacePoints.Count
            ? config.PlacePoints[slot - 1]
            : config.PointsBeyondTable;

    private static decimal ShareFor(IReadOnlyList<decimal> slotValues, TieRule rule) => rule switch
    {
        TieRule.HighestSlot => slotValues.Max(),
        TieRule.LowestSlot => slotValues.Min(),
        // NoTiesAllowed never reaches here with a group larger than one, and
        // for a single slot averaging is the identity anyway.
        _ => slotValues.Sum() / slotValues.Count,
    };

    private static TeamAward BuildAward(
        TeamEntry entry,
        int rank,
        IReadOnlyList<int> slots,
        IReadOnlyList<decimal> slotValues,
        decimal share,
        RoundInput input,
        ScoringConfig config)
    {
        decimal placementPoints;
        bool penalised;

        if (entry.IsDisqualified)
        {
            // A disqualified team's award is flat. The round multiplier is not
            // applied, because doubling a penalty in a double round surprises
            // people and serves no purpose.
            placementPoints = config.DqRule == DqRule.CustomPenalty ? config.DqPenaltyPoints : 0m;
            penalised = true;
        }
        else
        {
            placementPoints = share * input.Multiplier;
            penalised = false;
        }

        var total = Round(placementPoints + entry.Bonus, config);

        return new TeamAward(
            entry.TeamId,
            rank,
            total,
            entry.IsDisqualified,
            Explain(entry, rank, slots, slotValues, share, total, penalised, input, config));
    }

    private static string Explain(
        TeamEntry entry,
        int rank,
        IReadOnlyList<int> slots,
        IReadOnlyList<decimal> slotValues,
        decimal share,
        decimal total,
        bool penalised,
        RoundInput input,
        ScoringConfig config)
    {
        var parts = new List<string>();

        if (penalised)
        {
            parts.Add($"{Ordinal(rank)} place, disqualified, {Format(total, config)}.");
            parts.Add(config.DqRule == DqRule.DropToLast
                ? "Moved behind the other teams."
                : "Slot retained, so no team was promoted.");
        }
        else if (slots.Count == 1)
        {
            parts.Add($"{Ordinal(rank)} place, {Format(total, config)}.");
        }
        else
        {
            parts.Add($"Tied for {Ordinal(rank)}{TiedWith(entry, input)}.");

            var placeList = string.Join(" and ", slots.Select(Ordinal));
            var sum = string.Join(" + ", slotValues.Select(v => Format(v, config)));

            parts.Add(config.TieRule switch
            {
                TieRule.HighestSlot =>
                    $"Places {placeList} shared, best slot taken, {Format(share, config)} each.",
                TieRule.LowestSlot =>
                    $"Places {placeList} shared, lowest slot taken, {Format(share, config)} each.",
                _ =>
                    $"Places {placeList} share {sum} = {Format(slotValues.Sum(), config)}, split {slots.Count} ways, {Format(share, config)} each.",
            });
        }

        if (!penalised && input.Multiplier != 1m)
        {
            parts.Add($"Round multiplier {Format(input.Multiplier, config)} applied.");
        }

        if (entry.Bonus != 0m)
        {
            parts.Add($"Bonus {Format(entry.Bonus, config)} added.");
        }

        return string.Join(" ", parts);
    }

    private static string TiedWith(TeamEntry entry, RoundInput input)
    {
        var partners = input.Entries
            .Where(e => e.TeamId != entry.TeamId && e.Place == entry.Place)
            .ToList();

        if (partners.Count == 0) return string.Empty;

        var named = partners.Where(p => !string.IsNullOrWhiteSpace(p.DisplayName))
            .Select(p => p.DisplayName!)
            .ToList();

        if (named.Count != partners.Count)
        {
            return partners.Count == 1
                ? " with 1 other team"
                : $" with {partners.Count} other teams";
        }

        return named.Count == 1
            ? $" with {named[0]}"
            : $" with {string.Join(", ", named[..^1])} and {named[^1]}";
    }

    private static decimal Round(decimal value, ScoringConfig config) => Math.Round(
        value,
        config.Rounding.Decimals,
        config.Rounding.Mode == RoundingMode.HalfToEven
            ? MidpointRounding.ToEven
            : MidpointRounding.AwayFromZero);

    private static string Format(decimal value, ScoringConfig config) =>
        value.ToString("F" + config.Rounding.Decimals, CultureInfo.InvariantCulture);

    private static string Ordinal(int n)
    {
        var suffix = (n % 100) is >= 11 and <= 13
            ? "th"
            : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>
    /// Stable output order: by finishing place, absent teams last, then by team
    /// id so that shuffling the input can never change the output at all.
    /// </summary>
    private static IReadOnlyList<TeamAward> Order(IEnumerable<TeamAward> awards) =>
        awards
            .OrderBy(a => a.Place is null)
            .ThenBy(a => a.Place ?? int.MaxValue)
            .ThenBy(a => a.TeamId)
            .ToList();
}
