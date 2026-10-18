using System.Text.Json;
using System.Text.Json.Nodes;
using CloudConfigAgent.Core.Model;
using CloudConfigAgent.Core.Rules;

namespace CloudConfigAgent.Core.Agent;

/// <summary>
/// Werkzeuge, die der Agent aufrufen darf. Alle Werkzeuge sind lesend:
/// der Agent schlägt Korrekturen vor, führt sie aber nie selbst aus.
/// </summary>
public sealed class AgentTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message }, Json);

    private readonly ResourceSnapshot _snapshot;
    private readonly ComplianceEngine _engine;
    private IReadOnlyList<Finding>? _findings;

    public AgentTools(ResourceSnapshot snapshot, ComplianceEngine engine)
    {
        _snapshot = snapshot;
        _engine = engine;
    }

    public List<string> CallLog { get; } = new();

    public IReadOnlyList<Finding> Findings => _findings ??= _engine.Evaluate(_snapshot);

    /// <summary>Werkzeugbeschreibungen im Format der OpenAI-kompatiblen "tools"-Schnittstelle.</summary>
    public static JsonArray Definitions() => new()
    {
        Tool("list_resources", "Listet die Ressourcen des Kontos, optional gefiltert nach Typ.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["type"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("S3Bucket", "SecurityGroup", "EbsVolume")
                    }
                }
            }),
        Tool("run_compliance_checks", "Prüft alle Ressourcen gegen die Sicherheitsregeln und liefert die Befunde.",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        Tool("get_resource", "Liefert die vollständige Konfiguration einer Ressource.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["id"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray("id")
            })
    };

    private static JsonObject Tool(string name, string description, JsonObject parameters) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = parameters
        }
    };

    public string Execute(string name, string? argumentsJson)
    {
        CallLog.Add(name);
        JsonNode? args = null;
        try
        {
            args = string.IsNullOrWhiteSpace(argumentsJson) ? null : JsonNode.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return Error("Die Argumente sind kein gültiges JSON.");
        }

        return name switch
        {
            "list_resources" => ListResources(args?["type"]?.GetValue<string>()),
            "run_compliance_checks" => JsonSerializer.Serialize(Findings, Json),
            "get_resource" => GetResource(args?["id"]?.GetValue<string>()),
            _ => Error($"Unbekanntes Werkzeug: {name}")
        };
    }

    private string ListResources(string? type)
    {
        var items = new List<object>();
        if (type is null or "S3Bucket") items.AddRange(_snapshot.Buckets.Select(b => new { type = "S3Bucket", id = b.Name }));
        if (type is null or "SecurityGroup") items.AddRange(_snapshot.SecurityGroups.Select(g => new { type = "SecurityGroup", id = g.Id, name = g.Name }));
        if (type is null or "EbsVolume") items.AddRange(_snapshot.Volumes.Select(v => new { type = "EbsVolume", id = v.Id }));
        return JsonSerializer.Serialize(items, Json);
    }

    private string GetResource(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Error("Parameter 'id' fehlt.");
        }

        object? resource = _snapshot.Buckets.FirstOrDefault(b => b.Name == id)
            ?? (object?)_snapshot.SecurityGroups.FirstOrDefault(g => g.Id == id)
            ?? _snapshot.Volumes.FirstOrDefault(v => v.Id == id);

        return resource is null ? Error($"Ressource {id} nicht gefunden.") : JsonSerializer.Serialize(resource, resource.GetType(), Json);
    }
}
