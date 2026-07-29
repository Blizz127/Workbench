using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the Crimson Sands MMO server revival sample project so a fresh
/// install demonstrates packet-reverse-engineering workflows.
/// </summary>
public static class CrimsonSandsSeeder
{
    public const string ProjectName = "Crimson Sands Server Revival";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Server revival for Dark Sun Online: Crimson Sands. " +
                          "Rebuilding the game server from client binaries and archived " +
                          "packet captures so the surviving client can log in, load zones, and play D&D again.",
            RepositoryPath = "/home/blizz/Projects/dso-crimson-sands",
            BuildCommand = "dotnet build src/CrimsonServer",
            RunCommand = "dotnet run --project src/CrimsonServer -- --listen 0.0.0.0:2101",
            TestCommand = "dotnet test tests/CrimsonServer.Tests",
            DocumentationPaths = ["docs/packets/", "docs/zone-format.md", "docs/character-data.md"],
            AiContextPaths = ["docs/packets/opcode-table.md", "src/CrimsonServer/Protocol/", "notes/client-disassembly.md"],
            Tags = ["Game Preservation", "Server Revival", "Reverse Engineering", "MMO", "D&D"],
            ActiveTask = "Decode the character data opcode (0x1A)",
            CurrentObjective = "Get past character selection and into the starting zone",
            CreatedAt = now.AddDays(-90),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Login Handshake", true, 75),
            ("Server Select Screen", true, 50),
            ("Character List Response", true, 25),
            ("Character Data Opcode (0x1A)", false, 0),
            ("Zone Load — Free City of Tyr", false, 0),
            ("Combat System", false, 0),
        ];
        for (var i = 0; i < milestoneData.Length; i++)
        {
            var (title, done, daysAgo) = milestoneData[i];
            milestones.Add(new Milestone
            {
                ProjectId = project.Id,
                Title = title,
                IsCompleted = done,
                CompletedAt = done ? now.AddDays(-daysAgo) : null,
                SortOrder = i,
            });
        }

        // --- Frontiers ---
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Opcode 0x10 — server list response",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-15),
            CompletedAt = now.AddDays(-5),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Opcode 0x1A — character data block",
            Status = FrontierStatus.Current,
            Notes = "Payload starts with a 4-byte character ID, then variable-length fields. Need to map stat layout.",
            CreatedAt = now.AddDays(-3),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Zone handoff — Free City of Tyr",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Packet encryption XOR key changed mid-capture around opcode 0x10",
            IsResolved = true,
            Resolution = "Found the key rotation table in the client binary at offset 0x41B80",
            CreatedAt = now.AddDays(-14),
            ResolvedAt = now.AddDays(-4),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "0x1A character stat field layout unknown — appears to differ by race/class combo",
            CreatedAt = now.AddDays(-2),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (20, TimelineCategory.Frontier, "Started decoding opcode 0x10 server list"),
            (15, TimelineCategory.Blocker, "Encryption key mismatch on opcode 0x10"),
            (8, TimelineCategory.Build, "Build succeeded"),
            (5, TimelineCategory.Blocker, "Resolved encryption key rotation"),
            (5, TimelineCategory.Frontier, "Completed opcode 0x10 — server list parses cleanly"),
            (5, TimelineCategory.Milestone, "Milestone completed: Character List Response"),
            (3, TimelineCategory.Frontier, "Advanced to opcode 0x1A character data"),
            (2, TimelineCategory.Blocker, "Character stat layout unknown for non-human races"),
            (1, TimelineCategory.Build, "Build succeeded"),
        ];
        foreach (var (daysAgo, category, message) in timelineData)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Timestamp = now.AddDays(-daysAgo),
                Category = category,
                Message = message,
            });
        }

        // --- Notes ---
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Opcode catalog",
            Content =
                """
                # Known OPCodes

                | Opcode | Direction | Meaning | Status |
                |--------|-----------|---------|--------|
                | 0x01   | C→S       | Login request | done |
                | 0x02   | S→C       | Login result | done |
                | 0x10   | S→C       | Server list | done |
                | 0x1A   | S→C       | Character data | **in progress** |
                | 0x20   | C→S       | Zone enter request | todo |
                | 0x21   | S→C       | Zone data stream | todo |

                See `docs/packets/opcode-table.md` in the repo.
                """,
            CreatedAt = now.AddDays(-6),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Race/class stat layout cheat sheet",
            Content =
                """
                # Character Stat Layout (tentative)

                All characters start with:
                - 4 bytes: character ID
                - 2 bytes: name length + name (null-terminated)
                - 2 bytes: level

                After that, layout diverges:
                - Human Fighter: 6 stats × 2 bytes = 12 bytes
                - Elf Mage: 6 stats × 2 bytes + 2 bytes mana = 14 bytes
                - Dwarf Cleric: 6 stats × 2 bytes + 2 bytes divine favor = 14 bytes

                Need captures of all race/class combos to confirm.
                """,
            CreatedAt = now.AddDays(-1),
        });
    }
}
