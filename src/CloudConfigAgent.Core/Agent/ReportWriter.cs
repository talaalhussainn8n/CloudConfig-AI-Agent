
using System.Text;
using CloudConfigAgent.Core.Model;

namespace CloudConfigAgent.Core.Agent;

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

        foreach (var group in findings
                     .GroupBy(f => f.Severity)
                     .OrderByDescending(g => g.Key))
        {
            sb.AppendLine($"## {German(group.Key)} ({group.Count()})");
            sb.AppendLine();

            foreach (var f in group)
            {
                sb.AppendLine(
                    $"- **{f.ResourceId}** ({f.ResourceType}, Regel {f.RuleId}): {f.Title}");
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
