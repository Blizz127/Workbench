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
    bool TimedOut,
    bool Cancelled = false,
    bool OutputTruncated = false);

public interface IProcessRunner
{
    /// <summary>
    /// Runs a shell command in <paramref name="workingDirectory"/>, capturing
    /// stdout and stderr interleaved. Never throws; failures to start are
    /// reported with a null exit code and the error in the output, and
    /// cancellation kills the process tree and returns a Cancelled result.
    /// </summary>
    Task<ProcessResult> RunAsync(
        string command,
        string? workingDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cross-platform shell command runner (cmd.exe on Windows, /bin/sh elsewhere).
/// Output capture is bounded: the first <c>headLimitChars</c> and the last
/// <c>tailLimitChars</c> are kept, with a marker line noting how much was
/// dropped in between — a runaway command cannot grow memory or the history
/// database without bound.
/// </summary>
public sealed class ProcessRunner(int headLimitChars = 200_000, int tailLimitChars = 200_000) : IProcessRunner
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
        var output = new BoundedOutputBuffer(headLimitChars, tailLimitChars);

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
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

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
                var cancelled = cancellationToken.IsCancellationRequested;
                output.AppendLine(cancelled
                    ? "[workbench] command cancelled"
                    : "[workbench] command timed out");
                return new ProcessResult(
                    command, startedAt, null, stopwatch.Elapsed, output.Build(),
                    TimedOut: !cancelled, Cancelled: cancelled, OutputTruncated: output.Truncated);
            }

            stopwatch.Stop();
            return new ProcessResult(
                command, startedAt, process.ExitCode, stopwatch.Elapsed, output.Build(),
                TimedOut: false, OutputTruncated: output.Truncated);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            output.AppendLine($"[workbench] failed to start process: {ex.Message}");
            return new ProcessResult(
                command, startedAt, null, stopwatch.Elapsed, output.Build(),
                TimedOut: false, OutputTruncated: output.Truncated);
        }
    }

    /// <summary>
    /// Keeps the first <c>headLimit</c> characters verbatim; once the head is
    /// full, keeps a sliding window of the most recent lines up to
    /// <c>tailLimit</c> characters. <see cref="Truncated"/> is true only when
    /// content was actually dropped.
    /// </summary>
    private sealed class BoundedOutputBuffer(int headLimit, int tailLimit)
    {
        private readonly StringBuilder _head = new();
        private readonly Queue<string> _tail = new();
        private readonly object _lock = new();
        private int _tailChars;
        private long _droppedLines;

        public bool Truncated
        {
            get { lock (_lock) return _droppedLines > 0; }
        }

        public void AppendLine(string line)
        {
            lock (_lock)
            {
                if (_tail.Count == 0 && _droppedLines == 0 && _head.Length + line.Length + 1 <= headLimit)
                {
                    _head.AppendLine(line);
                    return;
                }

                // A single line larger than the whole tail budget is cut to fit.
                if (line.Length + 1 > tailLimit)
                {
                    line = line[..Math.Max(0, tailLimit - 1)];
                    _droppedLines++;
                }

                _tail.Enqueue(line);
                _tailChars += line.Length + 1;
                while (_tailChars > tailLimit && _tail.Count > 1)
                {
                    _tailChars -= _tail.Dequeue().Length + 1;
                    _droppedLines++;
                }
            }
        }

        public string Build()
        {
            lock (_lock)
            {
                var sb = new StringBuilder(_head.Length + _tailChars + 80);
                sb.Append(_head);
                if (_droppedLines > 0)
                    sb.AppendLine($"[workbench] output truncated: {_droppedLines} line(s) omitted");
                foreach (var line in _tail)
                    sb.AppendLine(line);
                return sb.ToString();
            }
        }
    }
}
