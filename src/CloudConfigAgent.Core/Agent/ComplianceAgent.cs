using System.Text.Json.Nodes;
using CloudConfigAgent.Core.Model;

namespace CloudConfigAgent.Core.Agent;

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