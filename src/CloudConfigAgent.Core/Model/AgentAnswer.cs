namespace CloudConfigAgent.Core.Model;

public sealed record AgentAnswer(string Text, IReadOnlyList<string> ToolCalls, bool ByLlm, IReadOnlyList<Finding> Findings);