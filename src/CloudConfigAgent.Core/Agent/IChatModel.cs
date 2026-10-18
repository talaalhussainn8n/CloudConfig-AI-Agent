using System.Text.Json.Nodes;

namespace CloudConfigAgent.Core.Agent;

/// <summary>Chat-Modell mit Werkzeugaufrufen. Liefert die Antwortnachricht des Assistenten.</summary>
public interface IChatModel
{
    bool IsConfigured { get; }
    Task<JsonObject> ChatAsync(JsonArray messages, JsonArray tools, CancellationToken ct);
}
