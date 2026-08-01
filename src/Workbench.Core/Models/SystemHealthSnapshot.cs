namespace Workbench.Core.Models;

/// <summary>Read-only health information for the machine hosting Workbench.</summary>
public sealed record SystemHealthSnapshot(
    double CpuUsagePercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes,
    long UsedDiskBytes,
    long TotalDiskBytes,
    TimeSpan Uptime)
{
    public double MemoryUsagePercent => Percentage(UsedMemoryBytes, TotalMemoryBytes);
    public double DiskUsagePercent => Percentage(UsedDiskBytes, TotalDiskBytes);

    private static double Percentage(long used, long total) =>
        total <= 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);
}
