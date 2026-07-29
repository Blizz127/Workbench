using System.Diagnostics;
using System.Text;

namespace Workbench.Core.Services;

/// <summary>Result of running an external command.</summary>
public sealed record ProcessResult(
    string Command,
    DateTimeOffset StartedAt,
    int? ExitCode,
    TimeSpan Duration,
    string Output,
    bool TimedOut);

public interface IProcessRunner
{
    /// <summary>
    /// Runs a shell command in <paramref name="workingDirectory"/>, capturing
    /// stdout and stderr interleaved. Never throws; failures to start are
    /// reported with a null exit code and the error in the output.
    /// </summary>
    Task<ProcessResult> RunAsync(
        string command,
        string? workingDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Cross-platform shell command runner (cmd.exe on Windows, /bin/sh elsewhere).</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    public async Task<ProcessResult> RunAsync(
        string command,
        string? workingDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var output = new StringBuilder();

        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c {command}")
            : new ProcessStartInfo("/bin/sh", ["-c", command]);

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        using var process = new Process { StartInfo = psi };
        var sync = new object();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (sync) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (sync) output.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? DefaultTimeout);

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                stopwatch.Stop();
                lock (sync) output.AppendLine(cancellationToken.IsCancellationRequested
                    ? "[workbench] command cancelled"
                    : "[workbench] command timed out");
                string partial;
                lock (sync) partial = output.ToString();
                return new ProcessResult(command, startedAt, null, stopwatch.Elapsed, partial, !cancellationToken.IsCancellationRequested);
            }

            stopwatch.Stop();
            string captured;
            lock (sync) captured = output.ToString();
            return new ProcessResult(command, startedAt, process.ExitCode, stopwatch.Elapsed, captured, false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            lock (sync) output.AppendLine($"[workbench] failed to start process: {ex.Message}");
            string captured;
            lock (sync) captured = output.ToString();
            return new ProcessResult(command, startedAt, null, stopwatch.Elapsed, captured, false);
        }
    }
}
