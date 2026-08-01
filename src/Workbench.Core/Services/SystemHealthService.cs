using System.Globalization;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

public interface ISystemHealthService
{
    Task<SystemHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads host metrics without invoking a shell or requiring elevated access.
/// Linux uses procfs; portable framework fallbacks keep the desktop app usable
/// on Windows and macOS while richer platform collectors can be added later.
/// </summary>
public sealed class SystemHealthService : ISystemHealthService
{
    private readonly string _diskPath;
    private readonly TimeSpan _cpuSampleInterval;

    public SystemHealthService(string? diskPath = null, TimeSpan? cpuSampleInterval = null)
    {
        _diskPath = diskPath ?? Path.GetPathRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory;
        _cpuSampleInterval = cpuSampleInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task<SystemHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var firstCpu = ReadLinuxCpu();
        if (firstCpu is not null)
            await Task.Delay(_cpuSampleInterval, cancellationToken);
        var secondCpu = ReadLinuxCpu();

        var cpu = CpuPercent(firstCpu, secondCpu);
        var (usedMemory, totalMemory) = ReadMemory();
        var (usedDisk, totalDisk) = ReadDisk();
        var uptime = ReadUptime();

        return new SystemHealthSnapshot(cpu, usedMemory, totalMemory, usedDisk, totalDisk, uptime);
    }

    private static (long Idle, long Total)? ReadLinuxCpu()
    {
        try
        {
            var fields = File.ReadLines("/proc/stat").First()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || fields[0] != "cpu")
                return null;

            var values = fields.Skip(1)
                .Select(value => long.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            return (idle, values.Sum());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double CpuPercent((long Idle, long Total)? first, (long Idle, long Total)? second)
    {
        if (first is not { } a || second is not { } b || b.Total <= a.Total)
            return 0;

        var totalDelta = b.Total - a.Total;
        var idleDelta = b.Idle - a.Idle;
        return Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static (long Used, long Total) ReadMemory()
    {
        try
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0],
                    parts => long.Parse(parts[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture) * 1024,
                    StringComparer.Ordinal);
            var total = values.GetValueOrDefault("MemTotal");
            var available = values.GetValueOrDefault("MemAvailable");
            return (Math.Max(0, total - available), total);
        }
        catch (Exception)
        {
            var info = GC.GetGCMemoryInfo();
            var total = Math.Max(0, info.TotalAvailableMemoryBytes);
            return (Math.Min(GC.GetTotalMemory(false), total), total);
        }
    }

    private (long Used, long Total) ReadDisk()
    {
        try
        {
            var drive = new DriveInfo(_diskPath);
            return (drive.TotalSize - drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private static TimeSpan ReadUptime()
    {
        try
        {
            var seconds = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture);
            return TimeSpan.FromSeconds(seconds);
        }
        catch (Exception)
        {
            return TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64));
        }
    }
}
