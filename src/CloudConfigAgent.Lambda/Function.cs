using Amazon.Lambda.Core;
using CloudConfigAgent.Core.Agent;
using CloudConfigAgent.Core.Model;
using CloudConfigAgent.Core.Providers;
using CloudConfigAgent.Core.Rules;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CloudConfigAgent.Lambda;

public sealed record CheckRequest(string? Question);

public sealed record CheckResponse(string Region, int FindingCount, int Critical, string Report, IReadOnlyList<Finding> Findings);

/// <summary>
/// Lambda-Funktion für eine regelmäßige Prüfung (z. B. täglich per EventBridge).
/// Liest die Konfiguration der eigenen Region und liefert Befunde und Bericht.
/// </summary>
public sealed class Function
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<CheckResponse> FunctionHandler(CheckRequest? request, ILambdaContext context)
    {
        var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "eu-central-1";
        var snapshot = await new AwsSnapshotProvider(region).GetSnapshotAsync();
        var tools = new AgentTools(snapshot, new ComplianceEngine());

        var llm = new LlmOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL"),
            ApiKey = Environment.GetEnvironmentVariable("LLM_API_KEY")
        };
        var agent = new ComplianceAgent(new OpenAiChatModel(Http, llm));
        var answer = await agent.AskAsync(request?.Question ?? "Fasse die wichtigsten Risiken zusammen.", tools);

        context.Logger.LogInformation($"Prüfung abgeschlossen: {answer.Findings.Count} Befunde in {region}");
        return new CheckResponse(
            region,
            answer.Findings.Count,
            answer.Findings.Count(f => f.Severity == Severity.Critical),
            answer.Text,
            answer.Findings);
    }
}
