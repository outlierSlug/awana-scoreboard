using System.Text.Json;

namespace Awana.Data.Entities;

/// <summary>
/// An append-only record of who changed what.
///
/// Written for the mutations that change a score: starting or finishing a
/// session, recording, editing or voiding a round, and adjusting points. Enough
/// to answer "why does Blue have 145" on a Monday morning without guessing.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Church Church { get; set; } = null!;

    /// <summary>Null for anything the system did on its own, such as seeding.</summary>
    public Guid? ActorUserId { get; set; }
    public AppUser? ActorUser { get; set; }

    /// <summary>A stable verb, for example "round.voided".</summary>
    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>
    /// Whatever detail is worth keeping, as jsonb. Deliberately unstructured:
    /// this is a record for a human reading it later, not something the
    /// application queries by field.
    /// </summary>
    public JsonDocument? Data { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
