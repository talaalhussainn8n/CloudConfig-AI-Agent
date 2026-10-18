using System.Text.Json.Nodes;
using CloudConfigAgent.Core.Agent;
using CloudConfigAgent.Core.Model;
using CloudConfigAgent.Core.Providers;
using CloudConfigAgent.Core.Rules;

namespace CloudConfigAgent.Tests;

public class AgentTests
{
    private static async Task<AgentTools> Tools()
    {
        var snapshot = await new JsonSnapshotProvider(Path.Combine(AppContext.BaseDirectory, "samples", "snapshot.json")).GetSnapshotAsync();
        return new AgentTools(snapshot, new ComplianceEngine());
    }

    /// <summary>Spielt vorgegebene Antworten eines Sprachmodells ab und merkt sich die Nachrichten.</summary>
    private sealed class ScriptedModel : IChatModel
    {
        private readonly Queue<JsonObject> _replies;
        public ScriptedModel(params JsonObject[] replies) => _replies = new Queue<JsonObject>(replies);
        public bool IsConfigured => true;
        public List<JsonArray> Seen { get; } = new();

        public Task<JsonObject> ChatAsync(JsonArray messages, JsonArray tools, CancellationToken ct)
        {
            Seen.Add((JsonArray)messages.DeepClone());
            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : ToolCall("call-x", "list_resources", "{}"));
        }
    }

    private sealed class NoModel : IChatModel
    {
        public bool IsConfigured => false;
        public Task<JsonObject> ChatAsync(JsonArray messages, JsonArray tools, CancellationToken ct) => throw new InvalidOperationException();
    }

    private static JsonObject ToolCall(string id, string name, string arguments) => new()
    {
        ["role"] = "assistant",
        ["content"] = null,
        ["tool_calls"] = new JsonArray(new JsonObject
        {
            ["id"] = id,
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
        })
    };

    private static JsonObject Final(string text) => new() { ["role"] = "assistant", ["content"] = text };

    [Fact]
    public async Task Agent_ruft_Werkzeug_auf_und_antwortet_danach()
    {
        var model = new ScriptedModel(
            ToolCall("call-1", "run_compliance_checks", "{}"),
            Final("Zuerst SSH in sg-0a1b2c3d schließen."));
        var tools = await Tools();

        var answer = await new ComplianceAgent(model).AskAsync("Was ist am dringendsten?", tools);

        Assert.True(answer.ByLlm);
        Assert.Equal("Zuerst SSH in sg-0a1b2c3d schließen.", answer.Text);
        Assert.Equal(new[] { "run_compliance_checks" }, answer.ToolCalls);

        // Das Werkzeugergebnis wurde als "tool"-Nachricht mit passender ID zurückgegeben.
        var second = model.Seen[1];
        var toolMessage = second.Last()!.AsObject();
        Assert.Equal("tool", toolMessage["role"]!.GetValue<string>());
        Assert.Equal("call-1", toolMessage["tool_call_id"]!.GetValue<string>());
        Assert.Contains("sg-0a1b2c3d", toolMessage["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Agent_bricht_nach_maximaler_Schrittzahl_ab()
    {
        var model = new ScriptedModel(); // ruft endlos Werkzeuge auf
        var answer = await new ComplianceAgent(model).AskAsync("Endlosschleife?", await Tools());

        Assert.False(answer.ByLlm);
        Assert.Equal(ComplianceAgent.MaxSteps, model.Seen.Count);
        Assert.Contains("maximale Anzahl", answer.Text);
    }

    [Fact]
    public async Task Ohne_Sprachmodell_kommt_ein_strukturierter_Bericht()
    {
        var answer = await new ComplianceAgent(new NoModel()).AskAsync("egal", await Tools());

        Assert.False(answer.ByLlm);
        Assert.StartsWith("# Prüfbericht: 13 Befunde", answer.Text);
        Assert.Contains("## Kritisch (3)", answer.Text);
    }

    [Fact]
    public async Task Werkzeuge_liefern_Ressourcen_und_Fehler()
    {
        var tools = await Tools();

        Assert.Contains("marketing-assets", tools.Execute("list_resources", "{\"type\":\"S3Bucket\"}"));
        Assert.DoesNotContain("sg-0a1b2c3d", tools.Execute("list_resources", "{\"type\":\"S3Bucket\"}"));
        Assert.Contains("\"encrypted\":false", tools.Execute("get_resource", "{\"id\":\"vol-0456efgh\"}"));
        Assert.Contains("nicht gefunden", tools.Execute("get_resource", "{\"id\":\"gibt-es-nicht\"}"));
        Assert.Contains("Unbekanntes Werkzeug", tools.Execute("delete_bucket", "{}"));
        Assert.Contains("kein gültiges JSON", tools.Execute("get_resource", "{kaputt"));
    }

    [Fact]
    public void Werkzeugdefinitionen_sind_vollstaendig()
    {
        var names = AgentTools.Definitions()
            .Select(t => t!["function"]!["name"]!.GetValue<string>())
            .ToList();

        Assert.Equal(new[] { "list_resources", "run_compliance_checks", "get_resource" }, names);
    }
}
