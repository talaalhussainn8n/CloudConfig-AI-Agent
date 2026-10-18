using CloudConfigAgent.Core.Model;

namespace CloudConfigAgent.Core.Providers;

public interface ISnapshotProvider
{
    Task<ResourceSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
