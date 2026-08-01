using System.Diagnostics;

namespace Workbench.Core.Services;

/// <summary>
/// Opens graphical terminals — a plain shell at a directory, or a terminal
/// running one command (e.g. <c>git log</c>, <c>tmux attach-session</c>).
/// Desktop-specific knowledge (KDE's konsole, Xfce's terminal, ...) lives
/// here so the rest of the app never shells out to a terminal directly.
/// </summary>
public interface ITerminalLauncher
{
    /// <summary>Installed terminal emulators, in preference order.</summary>
    IReadOnlyList<string> DetectAvailable();

    /// <summary>
    /// Opens a terminal at <paramref name="workingDirectory"/>, optionally
    /// running <paramref name="shellCommand"/> in it. Returns false when no
    /// terminal emulator is available (or the OS refused to launch one).
    /// </summary>
    bool Open(string workingDirectory, string? shellCommand = null);
}

/// <summary>One supported terminal and how it takes a command to run.</summary>
public sealed record TerminalCandidate(string Name, string ArgumentsTemplate);

public sealed class TerminalLauncher : ITerminalLauncher
{
    /// <summary>
    /// Supported terminals in preference order (Xfce/KDE first, per the Phase
    /// 1D spec, then the generic Debian alternative, then the rest).
    /// <c>{command}</c> marks where a command line goes. xfce4-terminal 1.x
    /// parses <c>-e</c> as one argument, so its command is quoted; konsole,
    /// the x-terminal-emulator wrapper, and the rest consume the remainder of
    /// the line xterm-style. Commands here are app-generated (validated tmux
    /// names, <c>git log</c>), never free-form user text, so quoting is safe.
    /// </summary>
    private static readonly TerminalCandidate[] Candidates =
    [
        new("xfce4-terminal", "-e \"{command}\""),
        new("konsole", "-e {command}"),
        new("x-terminal-emulator", "-e {command}"),
        new("ptyxis", "-- {command}"),
        new("gnome-terminal", "-- {command}"),
        new("kitty", "{command}"),
        new("alacritty", "-e {command}"),
        new("foot", "{command}"),
    ];

    /// <summary>
    /// Preference-ordered subset of <paramref name="availableNames"/>, with a
    /// <c>$TERMINAL</c> override promoted to the front when present.
    /// Pure and separately testable — no processes are launched here.
    /// </summary>
    public static IReadOnlyList<TerminalCandidate> OrderCandidates(
        IReadOnlyCollection<string> availableNames, string? terminalOverride)
    {
        var ordered = new List<TerminalCandidate>();
        if (!string.IsNullOrWhiteSpace(terminalOverride) && availableNames.Contains(terminalOverride))
            ordered.Add(new TerminalCandidate(terminalOverride, "-e {command}"));

        ordered.AddRange(Candidates.Where(c => availableNames.Contains(c.Name)));
        return ordered;
    }

    public IReadOnlyList<string> DetectAvailable() =>
        Candidates.Select(c => c.Name).Where(IsOnPath).ToList();

    public bool Open(string workingDirectory, string? shellCommand = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return false;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = shellCommand is null ? string.Empty : $"/k {shellCommand}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                });
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                // Terminal.app takes no command this way; open it at the directory instead.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { "-a", "Terminal", workingDirectory },
                });
                return true;
            }

            // Try each available terminal in preference order until one launches.
            foreach (var candidate in OrderCandidates(DetectAvailable(), Environment.GetEnvironmentVariable("TERMINAL")))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = candidate.Name,
                        Arguments = shellCommand is null
                            ? string.Empty
                            : candidate.ArgumentsTemplate.Replace("{command}", shellCommand),
                        WorkingDirectory = workingDirectory,
                    });
                    return true;
                }
                catch (Exception)
                {
                    // Not launchable despite being on PATH; try the next one.
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether an executable of this name is on the PATH (no process spawned).</summary>
    private static bool IsOnPath(string name)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
            return false;

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, name)))
                    return true;
            }
            catch (Exception)
            {
                // Unreadable PATH entry; keep looking.
            }
        }

        return false;
    }
}
