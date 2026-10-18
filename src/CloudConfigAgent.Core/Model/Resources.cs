using System.Text.Json.Serialization;

namespace CloudConfigAgent.Core.Model;

public sealed record S3Bucket(
    string Name,
    bool BlockPublicAccess,
    bool Encrypted,
    bool Versioning);

public sealed record IngressRule(
    string Protocol,
    int FromPort,
    int ToPort,
    string Cidr);

public sealed record SecurityGroup(
    string Id,
    string Name,
    IReadOnlyList<IngressRule> Ingress);

public sealed record EbsVolume(
    string Id,
    bool Encrypted,
    string? AttachedTo);

/// <summary>
/// Momentaufnahme der Konfiguration eines AWS-Kontos in einer Region.
/// Kann live über das AWS SDK erzeugt oder aus einer JSON-Datei geladen werden.
/// </summary>
public sealed record ResourceSnapshot(
    string Account,
    string Region,
    IReadOnlyList<S3Bucket> Buckets,
    IReadOnlyList<SecurityGroup> SecurityGroups,
    IReadOnlyList<EbsVolume> Volumes)
{
    public static ResourceSnapshot Empty(string account, string region) =>
        new(account, region, Array.Empty<S3Bucket>(), Array.Empty<SecurityGroup>(), Array.Empty<EbsVolume>());
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Severity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>Ein Verstoß gegen eine Konfigurationsregel mit Empfehlung und Korrekturbefehl.</summary>
public sealed record Finding(
    string RuleId,
    Severity Severity,
    string ResourceType,
    string ResourceId,
    string Title,
    string Recommendation,
    string FixCommand);
