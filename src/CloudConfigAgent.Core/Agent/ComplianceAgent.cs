using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using CloudConfigAgent.Core.Model;

namespace CloudConfigAgent.Core.Agent;

public sealed class LlmOptions
{
    /// <summary>OpenAI-kompatibler Endpunkt mit Unterstützung für Tool Calling, z. B. https://api.openai.com/v1.</summary>
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
}

/// <summary>Chat-Modell mit Werkzeugaufrufen. Liefert die Antwortnachricht des Assistenten.</summary>
public interface IChatModel
{
    bool IsConfigured { get; }
    Task<JsonObject> ChatAsync(JsonArray messages, JsonArray tools, CancellationToken ct);
}

public sealed class OpenAiChatModel : IChatModel
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;

    public OpenAiChatModel(HttpClient http, LlmOptions options)
    {
        _http = http;
        _options = options;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public async Task<JsonObject> ChatAsync(JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["temperature"] = 0,
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools.DeepClone()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl!.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["choices"]?[0]?["message"]?.AsObject().DeepClone().AsObject()
               ?? throw new InvalidOperationException("Unerwartete Antwort des Sprachmodells.");
    }
}

public sealed record AgentAnswer(string Text, IReadOnlyList<string> ToolCalls, bool ByLlm, IReadOnlyList<Finding> Findings);

/// <summary>
/// Agent, der eine Frage zur Cloud-Konfiguration beantwortet, indem er selbst entscheidet,
/// welche Werkzeuge er aufruft. Ohne Sprachmodell wird ein strukturierter Bericht erstellt.
/// </summary>
public sealed class ComplianceAgent
{
    public const int MaxSteps = 6;

    private const string SystemPrompt =
        "Du bist ein Cloud-Sicherheitsassistent für AWS. Nutze die Werkzeuge, um Fakten zu sammeln, bevor du antwortest. " +
        "Antworte auf Deutsch, priorisiere nach Schweregrad, nenne betroffene Ressourcen und den passenden Korrekturbefehl. " +
        "Du führst niemals Änderungen selbst aus, sondern schlägst sie nur vor.";

    private readonly IChatModel _model;

    public ComplianceAgent(IChatModel model) => _model = model;

    public async Task<AgentAnswer> AskAsync(string question, AgentTools tools, CancellationToken ct = default)
    {
        if (!_model.IsConfigured)
        {
            return new AgentAnswer(ReportWriter.Markdown(tools.Findings), Array.Empty<string>(), false, tools.Findings);
        }

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = question }
        };
        var definitions = AgentTools.Definitions();

        for (var step = 0; step < MaxSteps; step++)
        {
            var reply = await _model.ChatAsync(messages, definitions, ct);
            messages.Add(reply.DeepClone());

            var calls = reply["tool_calls"]?.AsArray();
            if (calls is null || calls.Count == 0)
            {
                var text = reply["content"]?.GetValue<string>() ?? string.Empty;
                return new AgentAnswer(text, tools.CallLog.ToList(), true, tools.Findings);
            }

            foreach (var call in calls)
            {
                var id = call?["id"]?.GetValue<string>() ?? string.Empty;
                var name = call?["function"]?["name"]?.GetValue<string>() ?? string.Empty;
                var arguments = call?["function"]?["arguments"]?.GetValue<string>();

                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = id,
                    ["content"] = tools.Execute(name, arguments)
                });
            }
        }

        // Schutz vor Endlosschleifen: nach MaxSteps den regelbasierten Bericht liefern.
        return new AgentAnswer(
            "Der Agent hat die maximale Anzahl an Schritten erreicht. Regelbasierter Bericht:\n\n" + ReportWriter.Markdown(tools.Findings),
            tools.CallLog.ToList(), false, tools.Findings);
    }
}

public static class ReportWriter
{
    public static string Markdown(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
        {
            return "Keine Befunde: alle geprüften Ressourcen erfüllen die Regeln.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Prüfbericht: {findings.Count} Befunde");
        sb.AppendLine();
        foreach (var group in findings.GroupBy(f => f.Severity).OrderByDescending(g => g.Key))
        {
            sb.AppendLine($"## {German(group.Key)} ({group.Count()})");
            sb.AppendLine();
            foreach (var f in group)
            {
                sb.AppendLine($"- **{f.ResourceId}** ({f.ResourceType}, Regel {f.RuleId}): {f.Title}");
                sb.AppendLine($"  - Empfehlung: {f.Recommendation}");
                sb.AppendLine($"  - Korrektur: `{f.FixCommand}`");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string German(Severity severity) => severity switch
    {
        Severity.Critical => "Kritisch",
        Severity.High => "Hoch",
        Severity.Medium => "Mittel",
        _ => "Niedrig"
    };
}
