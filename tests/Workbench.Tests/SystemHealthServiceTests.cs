using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class SystemHealthServiceTests
{
    [Fact]
    public async Task Snapshot_has_bounded_non_negative_host_metrics()
    {
        var service = new SystemHealthService(
            Path.GetPathRoot(Environment.CurrentDirectory),
            TimeSpan.FromMilliseconds(10));

        var snapshot = await service.GetSnapshotAsync();

        Assert.InRange(snapshot.CpuUsagePercent, 0, 100);
        Assert.InRange(snapshot.MemoryUsagePercent, 0, 100);
        Assert.InRange(snapshot.DiskUsagePercent, 0, 100);
        Assert.True(snapshot.TotalMemoryBytes >= 0);
        Assert.True(snapshot.TotalDiskBytes >= 0);
        Assert.True(snapshot.Uptime >= TimeSpan.Zero);
    }
}
