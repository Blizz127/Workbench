using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Small_output_is_captured_verbatim_and_not_truncated()
    {
        var runner = new ProcessRunner(headLimitChars: 1000, tailLimitChars: 1000);

        var result = await runner.RunAsync("echo first && echo last", workingDirectory: null);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.OutputTruncated);
        Assert.Contains("first", result.Output);
        Assert.Contains("last", result.Output);
        Assert.DoesNotContain("truncated", result.Output);
    }

    [Fact]
    public async Task Large_output_is_capped_keeping_head_and_tail_with_marker()
    {
        var runner = new ProcessRunner(headLimitChars: 2000, tailLimitChars: 2000);

        // ~59k chars of output: "line 1" .. "line 9999".
        var result = await runner.RunAsync(
            "i=1; while [ $i -le 9999 ]; do echo \"line $i\"; i=$((i+1)); done",
            workingDirectory: null);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.OutputTruncated);
        // Bounded: head + tail + marker, far below the raw ~59k.
        Assert.True(result.Output.Length < 5000, $"output was {result.Output.Length} chars");
        Assert.Contains("line 1\n", result.Output);          // head survived
        Assert.Contains("line 9999", result.Output);          // tail survived
        Assert.Contains("output truncated", result.Output);   // marker present
        Assert.DoesNotContain("line 5000\n", result.Output);  // middle dropped
    }

    [Fact]
    public async Task Oversized_single_line_is_cut_to_fit_the_tail_budget()
    {
        var runner = new ProcessRunner(headLimitChars: 100, tailLimitChars: 200);

        // One 5000-char line: longer than head and tail budgets combined.
        var result = await runner.RunAsync(
            "printf 'x%.0s' $(seq 1 5000); echo", workingDirectory: null);

        Assert.True(result.OutputTruncated);
        Assert.True(result.Output.Length < 600, $"output was {result.Output.Length} chars");
    }

    [Fact]
    public async Task Cancellation_kills_the_process_and_flags_the_result()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await runner.RunAsync("sleep 30", workingDirectory: null,
            cancellationToken: cts.Token);

        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.Contains("cancelled", result.Output);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10), "process was not killed promptly");
    }

    [Fact]
    public async Task Timeout_is_distinguished_from_cancellation()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync("sleep 30", workingDirectory: null,
            timeout: TimeSpan.FromMilliseconds(300));

        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Contains("timed out", result.Output);
    }
}
