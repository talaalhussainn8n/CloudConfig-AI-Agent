using System.Net;
using System.Text.Json;
using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using CloudConfigAgent.Core.Model;
using S3Bucket = CloudConfigAgent.Core.Model.S3Bucket;
using SecurityGroup = CloudConfigAgent.Core.Model.SecurityGroup;

namespace CloudConfigAgent.Core.Providers;

public interface ISnapshotProvider
{
    Task<ResourceSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

/// <summary>Lädt eine gespeicherte Momentaufnahme, z. B. für Demos, Tests oder Prüfungen ohne Kontozugriff.</summary>
public sealed class JsonSnapshotProvider : ISnapshotProvider
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public JsonSnapshotProvider(string path) => _path = path;

    public async Task<ResourceSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<ResourceSnapshot>(stream, Options, ct)
               ?? throw new InvalidDataException($"Die Datei {_path} enthält keine gültige Momentaufnahme.");
    }
}

/// <summary>
/// Liest die Konfiguration live über das AWS SDK for .NET.
/// Benötigt nur Leserechte (z. B. die verwaltete Richtlinie "SecurityAudit").
/// </summary>
public sealed class AwsSnapshotProvider : ISnapshotProvider
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonEC2 _ec2;
    private readonly string _region;

    public AwsSnapshotProvider(string region)
        : this(new AmazonS3Client(RegionEndpoint.GetBySystemName(region)),
               new AmazonEC2Client(RegionEndpoint.GetBySystemName(region)),
               region)
    {
    }

    public AwsSnapshotProvider(IAmazonS3 s3, IAmazonEC2 ec2, string region)
    {
        _s3 = s3;
        _ec2 = ec2;
        _region = region;
    }

    public async Task<ResourceSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var buckets = new List<S3Bucket>();
        var listed = await _s3.ListBucketsAsync(ct);
        foreach (var bucket in listed.Buckets ?? new List<Amazon.S3.Model.S3Bucket>())
        {
            buckets.Add(new S3Bucket(
                bucket.BucketName,
                await HasPublicAccessBlockAsync(bucket.BucketName, ct),
                await HasEncryptionAsync(bucket.BucketName, ct),
                await HasVersioningAsync(bucket.BucketName, ct)));
        }

        var groups = new List<SecurityGroup>();
        var sgResponse = await _ec2.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest(), ct);
        foreach (var sg in sgResponse.SecurityGroups ?? new List<Amazon.EC2.Model.SecurityGroup>())
        {
            var ingress = new List<IngressRule>();
            foreach (var permission in sg.IpPermissions ?? new List<IpPermission>())
            {
                foreach (var range in permission.Ipv4Ranges ?? new List<IpRange>())
                {
                    ingress.Add(new IngressRule(permission.IpProtocol, permission.FromPort, permission.ToPort, range.CidrIp));
                }
                foreach (var range in permission.Ipv6Ranges ?? new List<Ipv6Range>())
                {
                    ingress.Add(new IngressRule(permission.IpProtocol, permission.FromPort, permission.ToPort, range.CidrIpv6));
                }
            }
            groups.Add(new SecurityGroup(sg.GroupId, sg.GroupName, ingress));
        }

        var volumes = new List<EbsVolume>();
        var volResponse = await _ec2.DescribeVolumesAsync(new DescribeVolumesRequest(), ct);
        foreach (var v in volResponse.Volumes ?? new List<Volume>())
        {
            volumes.Add(new EbsVolume(v.VolumeId, v.Encrypted, v.Attachments?.FirstOrDefault()?.InstanceId));
        }

        return new ResourceSnapshot("aktuelles Konto", _region, buckets, groups, volumes);
    }

    private async Task<bool> HasPublicAccessBlockAsync(string bucket, CancellationToken ct)
    {
        try
        {
            var response = await _s3.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest { BucketName = bucket }, ct);
            var c = response.PublicAccessBlockConfiguration;
            return c is not null && c.BlockPublicAcls && c.IgnorePublicAcls && c.BlockPublicPolicy && c.RestrictPublicBuckets;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false; // Keine Konfiguration vorhanden.
        }
    }

    private async Task<bool> HasEncryptionAsync(string bucket, CancellationToken ct)
    {
        try
        {
            var response = await _s3.GetBucketEncryptionAsync(new GetBucketEncryptionRequest { BucketName = bucket }, ct);
            return response.ServerSideEncryptionConfiguration?.ServerSideEncryptionRules?.Count > 0;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> HasVersioningAsync(string bucket, CancellationToken ct)
    {
        var response = await _s3.GetBucketVersioningAsync(new GetBucketVersioningRequest { BucketName = bucket }, ct);
        return response.VersioningConfig?.Status == VersionStatus.Enabled;
    }
}
