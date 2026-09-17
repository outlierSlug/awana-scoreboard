using System.Text.Json;
using System.Text.Json.Nodes;

namespace Awana.Api.Services;

/// <summary>
/// Shapes the data an audit entry keeps.
/// </summary>
public static class AuditData
{
    /// <summary>
    /// The entry's data, with the session it happened in stamped on.
    /// </summary>
    /// <remarks>
    /// An entry about a round points at the round, and a round goes when its
    /// session is deleted. Without the session id kept alongside, the log would
    /// still say "Cleared round 2" but could no longer say on which night, and
    /// the activity view could not put it with the rest of that night.
    ///
    /// Stamped here, once, rather than at every call site, so an action added
    /// later cannot forget it.
    /// </remarks>
    public static JsonDocument ForSession(Guid sessionId, object data)
    {
        var node = JsonSerializer.SerializeToNode(data)?.AsObject() ?? new JsonObject();
        node["SessionId"] = JsonValue.Create(sessionId);
        return JsonSerializer.SerializeToDocument(node);
    }
}
