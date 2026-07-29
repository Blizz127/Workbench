using Workbench.Core.Models;
using Workbench.Data.Repositories;

namespace Workbench.Data.SampleData;

/// <summary>
/// Seeds the FFXI-EventsDump project — a Final Fantasy XI event bytecode parser
/// and analyzer. Tracks opcode registry coverage, control flow analysis, and
/// the remaining parsing gaps across 303 zones of dumped event data.
/// </summary>
public static class FfxiEventsDumpSeeder
{
    public const string ProjectName = "FFXI-EventsDump";

    public static void Seed(ProjectRepository projects, MilestoneRepository milestones,
        FrontierRepository frontiers, BlockerRepository blockers,
        TimelineRepository timeline, NoteRepository notes, DateTimeOffset now)
    {
        var project = new Project
        {
            Name = ProjectName,
            Status = ProjectStatus.Active,
            Description = "FFXI Event Bytecode Parser and Analyzer. Parses, analyzes, " +
                          "and renders human-readable output from Final Fantasy XI event " +
                          "bytecode (DAT files). Includes a full opcode registry (225+ opcodes), " +
                          "control flow analysis using networkx, string formatting with parameter " +
                          "substitution, entity/zone data models, and Jinja2 template output.",
            RepositoryPath = "/home/blizz/Projects/FFXI-EventsDump",
            BuildCommand = "pip install -e .",
            RunCommand = "xievents-parse --zone \"Aht Urhgan Whitegate\" --event 1",
            TestCommand = "pytest tests/ -v --cov=parser --cov=models --cov-report=term-missing",
            DocumentationPaths = ["README.md", "parser/opcodes/", "models/", "templates/"],
            AiContextPaths = ["parser/opcodes/registry.py", "parser/opcodes/base.py", "parser/control_flow.py", "parser/string_formatter.py", "parser/eventcode.py", "parser/eventdat.py", "parser/entitydat.py", "models/zones.py", "models/entities.py", "models/events.py", "models/strings.py", "dump_opcodes.py"],
            Tags = ["Game Preservation", "Reverse Engineering", "FFXI", "Bytecode Analysis", "Event Scripting"],
            ActiveTask = "Improve control flow analysis for multi-event CALL chains (0x1A → 65535)",
            CurrentObjective = "Parse all 303 zones of dumped event data with 100% opcode coverage and clean control flow",
            CreatedAt = now.AddDays(-14),
        };
        projects.Add(project);

        // --- Milestones ---
        (string Title, bool Done, int DaysAgo)[] milestoneData =
        [
            ("Opcode Registry (225 opcodes)", true, 13),
            ("Control Flow Analysis (networkx)", true, 10),
            ("String Formatter (param substitution)", true, 9),
            ("Entity/Zone Data Models", true, 8),
            ("Test Suite (control_flow + string_formatter)", true, 7),
            ("Full Zone Dump Pipeline", false, 0),
            ("Multi-event CALL Chains (0x1A → 65535)", false, 0),
            ("Item/Music/Magic ID Lookups", false, 0),
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
            Title = "Opcode registry: 225/225 implemented",
            Status = FrontierStatus.Completed,
            CreatedAt = now.AddDays(-13),
            CompletedAt = now.AddDays(-10),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Multi-event CALL chains (0x1A jumps into event 65535)",
            Status = FrontierStatus.Current,
            Notes = "Control flow analyzer handles intra-event jumps but not cross-event CALL/RETURN. Need to resolve 0x1A targets across the zone's event table.",
            CreatedAt = now.AddDays(-6),
        });
        frontiers.Add(new Frontier
        {
            ProjectId = project.Id,
            Title = "Item/Music/Magic ID lookups in string rendering",
            Status = FrontierStatus.Next,
            CreatedAt = now,
        });

        // --- Blockers ---
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Some events may not parse correctly — unknown opcode shapes in 0xD-range",
            IsResolved = true,
            Resolution = "Added calculate_length() overrides to variable-length opcodes (0x1F, 0x31, 0x47, 0x71, 0x8C, 0xD4, 0xD8, etc.) so the parser advances correctly.",
            CreatedAt = now.AddDays(-12),
            ResolvedAt = now.AddDays(-9),
        });
        blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Description = "Cross-event CALL chains (0x1A → event 65535) break control flow graph",
            CreatedAt = now.AddDays(-6),
        });

        // --- Timeline ---
        (int DaysAgo, TimelineCategory Category, string Message)[] timelineData =
        [
            (14, TimelineCategory.Code, "Scaffolded opcode registry loader (auto-discovers 0xXX_*.py)"),
            (13, TimelineCategory.Frontier, "Reached 225/225 opcodes implemented"),
            (12, TimelineCategory.Blocker, "Variable-length opcodes mis-parsed — wrong advance"),
            (10, TimelineCategory.Milestone, "Milestone completed: Control Flow Analysis"),
            (9, TimelineCategory.Code, "Added calculate_length() to 0xD-range opcodes"),
            (9, TimelineCategory.Blocker, "Resolved variable-length parsing"),
            (8, TimelineCategory.Milestone, "Milestone completed: Entity/Zone Data Models"),
            (7, TimelineCategory.Milestone, "Milestone completed: Test Suite"),
            (7, TimelineCategory.Build, "pytest passes — 2 test modules, 40+ assertions"),
            (6, TimelineCategory.Frontier, "Advanced to multi-event CALL chains"),
            (6, TimelineCategory.Blocker, "Cross-event CALL (0x1A) breaks CFG"),
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
            Title = "Opcode registry architecture",
            Content =
                """
                # Opcode Registry

                Each opcode is a single file `parser/opcodes/0xXX_name.py`
                implementing `BaseOpcode`:

                - `opcode` — the byte value
                - `name` — legible name
                - `get_args()` — argument layout (ArgType + sizes)
                - `calculate_length(data, offset)` — variable-length opcodes
                  MUST override this or the parser desyncs
                - `get_legible_representation()` — human-readable output
                - `get_jump_targets()` — for control flow (IF/GOTO/CALL)

                The registry auto-imports all `0x*.py` files at startup.

                **Key movement/position opcodes:**
                - `0x1F` MOVE_ENTITY
                - `0x31` UPDATE_ENTITY_POSITION
                - `0x36` SET_ENTITY_EVENT_POSITION
                - `0x39` SET_ENTITY_DIRECTION
                - `0x3B` GET_ENTITY_POSITION
                - `0x47` UPDATE_PLAYER_LOCATION
                - `0x4B` UPDATE_ENTITY_YAW
                - `0x5A` UPDATE_EVENT_POSITION
                - `0xBA` SET_ENTITY_POSITION
                """,
            CreatedAt = now.AddDays(-10),
        });
        notes.Add(new Note
        {
            ProjectId = project.Id,
            Title = "Control flow gaps",
            Content =
                """
                # Control Flow Gaps

                The `ControlFlowAnalyzer` builds a networkx DiGraph from
                intra-event jumps (0x01 GOTO, 0x02 IF, 0x1A CALL_SUBROUTINE).

                **Open problem:** `0x1A CALL_SUBROUTINE(address=0xXXXX)` can
                jump to a *different event* in the same zone's event table
                (e.g. event 65535 = shared subroutine library). The current
                analyzer only resolves offsets within the current event's
                bytecode, so cross-event CALLs produce dangling edges.

                **Fix approach:** load the full zone event table, map each
                event's entry offset, and resolve 0x1A targets against the
                cross-event offset space. RETURN (0x1B) then pops the call
                stack.

                See `tests/test_control_flow.py` for the intra-event cases
                that already pass.
                """,
            CreatedAt = now.AddDays(-6),
        });
    }
}
