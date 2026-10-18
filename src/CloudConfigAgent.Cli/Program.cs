using CloudConfigAgent.Core.Agent;
using CloudConfigAgent.Core.Providers;
using CloudConfigAgent.Core.Rules;

// Aufruf:
//   cloudconfig-agent --snapshot samples/snapshot.json [--question "Was ist am dringendsten?"]
//   cloudconfig-agent --aws --region eu-central-1 [--question "..."]
// Sprachmodell über Umgebungsvariablen LLM_BASE_URL, LLM_API_KEY, LLM_MODEL (optional).

var options = ParseArgs(args);
if (options.ContainsKey("help") || (!options.ContainsKey("snapshot") && !options.ContainsKey("aws")))
{
    Console.WriteLine("Nutzung: cloudconfig-agent (--snapshot <datei> | --aws --region <region>) [--question \"...\"]");
    return 1;
}

ISnapshotProvider provider = options.ContainsKey("aws")
    ? new AwsSnapshotProvider(options.GetValueOrDefault("region") ?? "eu-central-1")
    : new JsonSnapshotProvider(options["snapshot"]!);

var snapshot = await provider.GetSnapshotAsync();
var tools = new AgentTools(snapshot, new ComplianceEngine());

var llm = new LlmOptions
{
    BaseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL"),
    ApiKey = Environment.GetEnvironmentVariable("LLM_API_KEY"),
    Model = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gpt-4o-mini"
};
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
var agent = new ComplianceAgent(new OpenAiChatModel(http, llm));

var question = options.GetValueOrDefault("question") ?? "Welche Sicherheitsrisiken gibt es und was sollte zuerst behoben werden?";
var answer = await agent.AskAsync(question, tools);

Console.WriteLine($"Konto {snapshot.Account}, Region {snapshot.Region}");
if (answer.ByLlm)
{
    Console.WriteLine($"Genutzte Werkzeuge: {string.Join(", ", answer.ToolCalls)}");
}
Console.WriteLine();
Console.WriteLine(answer.Text);

// Exit-Code für CI-Pipelines: 2 bei kritischen Befunden.
return answer.Findings.Any(f => f.Severity == CloudConfigAgent.Core.Model.Severity.Critical) ? 2 : 0;

static Dictionary<string, string?> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
        {
            continue;
        }

        var key = args[i][2..];
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
        result[key] = hasValue ? args[++i] : null;
    }
    return result;
}
