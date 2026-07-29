using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the TEN Revival sample project — rebuilding a dead online-game
/// network service from captured protocol traffic.
/// </summary>
public static class TenRevivalSeeder
{
    public const string ProjectName = "TEN Revival";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "Revival of the Total Entertainment Network online service: " +
                          "reverse engineering the client protocol and reimplementing the lobby, " +
                          "matchmaking, and game relay servers so the original clients can connect again.",
            RepositoryPath = "/home/blizz/Projects/ten-revival",
            BuildCommand = "cmake --build build -j8",
            RunCommand = "./build/ten-server --config server.toml",
            TestCommand = "python3 tools/protocol_tests.py --captures captures/",
            DocumentationPaths = ["docs/protocol/", "docs/lobby-flow.md", "captures/README.md"],
            AiContextPaths = ["docs/protocol/message-catalog.md", "src/relay/", "include/ten_protocol.h"],
            Tags = ["Networking", "Game Preservation", "Reverse Engineering", "Server Revival"],
            ActiveTask = "Implement matchmaking queue message flow",
            CurrentObjective = "Two clients can find each other and enter a game session",
            CreatedAt = now.AddDays(-110),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("TCP Connection & Framing", true, 95),
            ("Authentication Handshake", true, 70),
            ("Lobby Listing", true, 40),
            ("Chat System", true, 10),
            ("Matchmaking Queue", false, 0),
            ("Game Relay Proxy", false, 0),
            ("Friends List", false, 0),
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
            Title = "MSG_CHAT — lobby chat message relay",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-18),
            CompletedAt = now.AddDays(-8),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "MSG_MATCHMAKE_REQ — queue join request",
            Status = FrontierStatus.Current,
            Notes = "Payload known: game_type (2 bytes) + skill_rating (2 bytes). Need to figure out the MATCH_FOUND response format.",
            CreatedAt = now.AddDays(-5),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "MSG_SESSION_KEY — game relay token exchange",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Chat messages scrambled after 512 bytes — fragmentation bug in frame reassembly",
            IsResolved = true,
            Resolution = "Frame header length field was 1 byte; expanded to 2 bytes for messages >255 payload bytes",
            CreatedAt = now.AddDays(-17),
            ResolvedAt = now.AddDays(-11),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "MATCH_FOUND response has an extra 8-byte token that doesn't appear in captures — crypto?",
            CreatedAt = now.AddDays(-2),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (18, TimelineCategory.Frontier, "Started MSG_CHAT relay"),
            (17, TimelineCategory.Blocker, "Chat fragmentation breaks after 512 bytes"),
            (12, TimelineCategory.Build, "Build succeeded"),
            (11, TimelineCategory.Blocker, "Resolved chat fragmentation — frame header now 2 bytes"),
            (10, TimelineCategory.Milestone, "Milestone completed: Chat System"),
            (8, TimelineCategory.Frontier, "Completed MSG_CHAT relay"),
            (7, TimelineCategory.Code, "Implemented lobby message routing table"),
            (5, TimelineCategory.Frontier, "Advanced to MSG_MATCHMAKE_REQ"),
            (3, TimelineCategory.Build, "Build succeeded — all protocol tests pass"),
            (2, TimelineCategory.Blocker, "MATCH_FOUND token unknown — suspected session key"),
            (1, TimelineCategory.Build, "Build timeout — cmake dependency refresh"),
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
            Title = "TEN protocol message catalog",
            Content =
                """
                # TEN Wire Protocol

                ## Message Header (6 bytes)
                - 2 bytes: message type
                - 2 bytes: payload length
                - 2 bytes: sequence number

                ## Known Message Types
                | Type   | Name | Direction | Status |
                |--------|------|-----------|--------|
                | 0x0001 | AUTH_REQ | C→S | done |
                | 0x0002 | AUTH_RESP | S→C | done |
                | 0x0010 | LOBBY_LIST | S→C | done |
                | 0x0020 | MSG_CHAT | Bidirectional | done |
                | 0x0030 | MATCHMAKE_REQ | C→S | in progress |
                | 0x0031 | MATCH_FOUND | S→C | in progress |
                | 0x0040 | SESSION_KEY | S→C | todo |

                See `docs/protocol/message-catalog.md` for full details.
                """,
            CreatedAt = now.AddDays(-7),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Capture replay notes",
            Content =
                """
                # Capture Analysis

                Most captures come from `captures/lobby-session-*.pcap` —
                circa 2003, captured on the TEN client side before the
                service shut down.

                Key captures:
                - `lobby-session-auth.pcap` — auth flow (done)
                - `lobby-session-chat.pcap` — chat messages (done)
                - `lobby-session-matchmaking.pcap` — incomplete; cuts off
                  before MATCH_FOUND. Need to find more captures or
                  reverse-engineer the token format from the client binary.
                """,
            CreatedAt = now.AddDays(-3),
        });
    }
}
