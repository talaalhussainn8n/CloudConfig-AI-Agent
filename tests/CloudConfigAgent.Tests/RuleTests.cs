using CloudConfigAgent.Core.Model;
using CloudConfigAgent.Core.Providers;
using CloudConfigAgent.Core.Rules;

namespace CloudConfigAgent.Tests;

public class RuleTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "snapshot.json");

    private static Task<ResourceSnapshot> Sample() => new JsonSnapshotProvider(SamplePath).GetSnapshotAsync();

    [Fact]
    public async Task Momentaufnahme_wird_geladen()
    {
        var snapshot = await Sample();

        Assert.Equal("eu-central-1", snapshot.Region);
        Assert.Equal(3, snapshot.Buckets.Count);
        Assert.Equal(3, snapshot.SecurityGroups.Count);
        Assert.Null(snapshot.Volumes[1].AttachedTo);
    }

    [Fact]
    public async Task Alle_erwarteten_Befunde_im_Beispielkonto()
    {
        var findings = new ComplianceEngine().Evaluate(await Sample());

        // 1x Public Access, 1x Verschlüsselung, 2x Versionierung, 1 + 7 offene Ports, 1x EBS
        Assert.Equal(13, findings.Count);
        Assert.Equal(3, findings.Count(f => f.Severity == Severity.Critical));
    }

    [Fact]
    public async Task Befunde_sind_nach_Schweregrad_sortiert()
    {
        var findings = new ComplianceEngine().Evaluate(await Sample());

        Assert.Equal(findings.OrderByDescending(f => f.Severity).Select(f => f.Severity), findings.Select(f => f.Severity));
        Assert.Equal("sg-0a1b2c3d", findings[0].ResourceId);
    }

    [Fact]
    public void Internes_Netz_ist_kein_Befund()
    {
        var snapshot = ResourceSnapshot.Empty("test", "eu-central-1") with
        {
            SecurityGroups = new[]
            {
                new SecurityGroup("sg-1", "intern", new[] { new IngressRule("tcp", 22, 22, "10.0.0.0/8") })
            }
        };

        Assert.Empty(new OpenSensitivePortRule().Evaluate(snapshot));
    }

    [Fact]
    public void Portbereich_mit_sensiblem_Port_wird_erkannt()
    {
        var snapshot = ResourceSnapshot.Empty("test", "eu-central-1") with
        {
            SecurityGroups = new[]
            {
                new SecurityGroup("sg-2", "bereich", new[] { new IngressRule("tcp", 3380, 3400, "::/0") })
            }
        };

        var finding = Assert.Single(new OpenSensitivePortRule().Evaluate(snapshot));
        Assert.Contains("RDP", finding.Title);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("--port 3389", finding.FixCommand);
    }

    [Fact]
    public async Task Korrekturbefehle_nennen_die_Ressource()
    {
        var findings = new ComplianceEngine().Evaluate(await Sample());
        Assert.All(findings.Where(f => f.RuleId.StartsWith("S3")), f => Assert.Contains(f.ResourceId, f.FixCommand));
    }
}
