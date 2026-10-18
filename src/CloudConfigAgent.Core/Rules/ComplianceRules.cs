using CloudConfigAgent.Core.Model;

namespace CloudConfigAgent.Core.Rules;

/// <summary>S3-Buckets ohne "Block Public Access" können versehentlich öffentlich werden.</summary>
public sealed class S3PublicAccessRule : IComplianceRule
{
    public string Id => "S3-001";
    public string Description => "S3-Buckets blockieren öffentlichen Zugriff";

    public IEnumerable<Finding> Evaluate(ResourceSnapshot snapshot) =>
        snapshot.Buckets.Where(b => !b.BlockPublicAccess).Select(b => new Finding(
            Id, Severity.High, "S3Bucket", b.Name,
            "Öffentlicher Zugriff ist nicht blockiert",
            "Block Public Access für den Bucket aktivieren, sofern er nicht bewusst öffentlich sein soll.",
            $"aws s3api put-public-access-block --bucket {b.Name} --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"));
}

public sealed class S3EncryptionRule : IComplianceRule
{
    public string Id => "S3-002";
    public string Description => "S3-Buckets sind standardmäßig verschlüsselt";

    public IEnumerable<Finding> Evaluate(ResourceSnapshot snapshot) =>
        snapshot.Buckets.Where(b => !b.Encrypted).Select(b => new Finding(
            Id, Severity.Medium, "S3Bucket", b.Name,
            "Keine Standardverschlüsselung",
            "Serverseitige Verschlüsselung (SSE-S3 oder SSE-KMS) als Standard setzen.",
            $"aws s3api put-bucket-encryption --bucket {b.Name} --server-side-encryption-configuration '{{\"Rules\":[{{\"ApplyServerSideEncryptionByDefault\":{{\"SSEAlgorithm\":\"AES256\"}}}}]}}'"));
}

public sealed class S3VersioningRule : IComplianceRule
{
    public string Id => "S3-003";
    public string Description => "S3-Buckets haben Versionierung aktiviert";

    public IEnumerable<Finding> Evaluate(ResourceSnapshot snapshot) =>
        snapshot.Buckets.Where(b => !b.Versioning).Select(b => new Finding(
            Id, Severity.Low, "S3Bucket", b.Name,
            "Versionierung ist deaktiviert",
            "Versionierung aktivieren, um versehentliches Löschen oder Überschreiben rückgängig machen zu können.",
            $"aws s3api put-bucket-versioning --bucket {b.Name} --versioning-configuration Status=Enabled"));
}

/// <summary>Administrative und Datenbank-Ports dürfen nicht aus dem gesamten Internet erreichbar sein.</summary>
public sealed class OpenSensitivePortRule : IComplianceRule
{
    public static readonly IReadOnlyDictionary<int, string> SensitivePorts = new Dictionary<int, string>
    {
        [22] = "SSH",
        [3389] = "RDP",
        [3306] = "MySQL",
        [5432] = "PostgreSQL",
        [1433] = "MS SQL Server",
        [6379] = "Redis",
        [27017] = "MongoDB"
    };

    public string Id => "EC2-001";
    public string Description => "Keine sensiblen Ports offen für 0.0.0.0/0 oder ::/0";

    public IEnumerable<Finding> Evaluate(ResourceSnapshot snapshot)
    {
        foreach (var group in snapshot.SecurityGroups)
        {
            foreach (var rule in group.Ingress.Where(IsWorldOpen))
            {
                var allPorts = rule.Protocol == "-1";
                var exposed = SensitivePorts
                    .Where(p => allPorts || (p.Key >= rule.FromPort && p.Key <= rule.ToPort))
                    .ToList();

                foreach (var (port, service) in exposed)
                {
                    yield return new Finding(
                        Id,
                        port is 22 or 3389 ? Severity.Critical : Severity.High,
                        "SecurityGroup",
                        group.Id,
                        $"{service} (Port {port}) ist aus dem Internet erreichbar",
                        "Zugriff auf bekannte IP-Bereiche beschränken oder über Session Manager bzw. VPN gehen.",
                        $"aws ec2 revoke-security-group-ingress --group-id {group.Id} --protocol {(allPorts ? "-1" : rule.Protocol)} --port {port} --cidr {rule.Cidr}");
                }
            }
        }
    }

    private static bool IsWorldOpen(IngressRule rule) => rule.Cidr is "0.0.0.0/0" or "::/0";
}

public sealed class EbsEncryptionRule : IComplianceRule
{
    public string Id => "EBS-001";
    public string Description => "EBS-Volumes sind verschlüsselt";

    public IEnumerable<Finding> Evaluate(ResourceSnapshot snapshot) =>
        snapshot.Volumes.Where(v => !v.Encrypted).Select(v => new Finding(
            Id, Severity.Medium, "EbsVolume", v.Id,
            "Volume ist nicht verschlüsselt",
            "Verschlüsselten Snapshot erstellen, daraus ein neues Volume anlegen und künftig Standardverschlüsselung aktivieren.",
            "aws ec2 enable-ebs-encryption-by-default"));
}

public sealed class ComplianceEngine
{
    private readonly IReadOnlyList<IComplianceRule> _rules;

    public ComplianceEngine(IEnumerable<IComplianceRule>? rules = null)
    {
        _rules = (rules ?? Default()).ToList();
    }

    public IReadOnlyList<IComplianceRule> Rules => _rules;

    public static IEnumerable<IComplianceRule> Default() => new IComplianceRule[]
    {
        new S3PublicAccessRule(), new S3EncryptionRule(), new S3VersioningRule(),
        new OpenSensitivePortRule(), new EbsEncryptionRule()
    };

    public IReadOnlyList<Finding> Evaluate(ResourceSnapshot snapshot) =>
        _rules.SelectMany(r => r.Evaluate(snapshot))
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.ResourceId, StringComparer.Ordinal)
            .ToList();
}
