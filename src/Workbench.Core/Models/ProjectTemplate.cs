namespace Workbench.Core.Models;

/// <summary>A suggested custom command slot a template proposes — created as a real <see cref="ProjectCommand"/> once the project exists.</summary>
public sealed record TemplateCommand(string Name, string CommandText);

/// <summary>
/// A starting point for a recurring kind of project: names, placeholders, and
/// suggested configuration to prefill when registering a new project — never
/// a repository path (a template must never assume a repo exists on this
/// machine) and never anything that calls an AI provider.
/// </summary>
public sealed record ProjectTemplate(
    string Key,
    string DisplayName,
    string Description,
    string ObjectivePlaceholder,
    string ActiveTaskPlaceholder,
    string? PreferredAiProvider,
    IReadOnlyList<string> AiContextPaths,
    string PromptRules,
    IReadOnlyList<LogPatternDefinition> CustomLogPatterns,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> DocumentationPaths,
    string? BuildCommand,
    string? RunCommand,
    string? TestCommand,
    string? MatchingBuildCommand,
    IReadOnlyList<TemplateCommand> SuggestedCommands);

/// <summary>The fixed catalog of built-in templates for the recurring project shapes this user tracks.</summary>
public static class ProjectTemplateCatalog
{
    public static IReadOnlyList<ProjectTemplate> All { get; } =
    [
        new(
            Key: "xenogears",
            DisplayName: "Xenogears Decompilation",
            Description: "Decompilation of Xenogears (PSX) toward a native PC port. Matching functions are verified against the retail binary.",
            ObjectivePlaceholder: "e.g. Get disc 1's opening sequence fully matching",
            ActiveTaskPlaceholder: "e.g. Matching the next flagged function in the field engine",
            PreferredAiProvider: "Claude (Sonnet) — decomp pairing",
            AiContextPaths: ["docs/decomp-guide.md", "include/"],
            PromptRules: "Never modify original decomp source under asm/ or src/ without matching evidence.\n" +
                         "Confirm matches via the matching-build command before claiming a function is done.\n" +
                         "Prefer minimal diffs; the PC port lives entirely under pc_port/.",
            CustomLogPatterns:
            [
                new LogPatternDefinition("RUN_RC=", "RUN_RC=", IsRegex: true, Category: "TestHarness"),
                new LogPatternDefinition("[stub]", @"\[stub\]", IsRegex: true, Category: "TestHarness"),
            ],
            Tags: ["Reverse Engineering", "Game Preservation", "PSX"],
            DocumentationPaths: ["docs/", "notes/memory-map.md"],
            BuildCommand: "./pc_port/build_port.sh",
            RunCommand: "./build/xenogears",
            TestCommand: "python3 tools/check_matches.py",
            MatchingBuildCommand: "make -B build",
            SuggestedCommands: [new TemplateCommand("field-repro", "./tools/field_repro.sh")]),

        new(
            Key: "parasite-eve",
            DisplayName: "Parasite Eve Decompilation",
            Description: "PS1 decompilation research project for Parasite Eve. Early phase: disc verification and setup, ahead of any disassembly or symbols.",
            ObjectivePlaceholder: "e.g. Finish disc verification for both discs",
            ActiveTaskPlaceholder: "e.g. Cross-checking disc hashes against an independent source",
            PreferredAiProvider: "Claude (Sonnet) — decomp pairing",
            AiContextPaths: ["docs/ai_context/ACTIVE_HANDOFF.md", "CLAUDE.md"],
            PromptRules: "Read docs/ai_context/ACTIVE_HANDOFF.md before doing anything.\n" +
                         "Never invent decompiled C; no code under src/ until the checksum/rebuild harness exists.\n" +
                         "Never claim matching progress without checksum/build verification and the exact command recorded.\n" +
                         "Never commit game data: no disc images, extracted files, assets, or proprietary SDK files.",
            CustomLogPatterns:
            [
                new LogPatternDefinition("disc-verify OK", "^OK: disc", IsRegex: true, Category: "DiscVerification"),
                new LogPatternDefinition("disc-verify SKIP", "^SKIP:", IsRegex: true, Category: "DiscVerification"),
            ],
            Tags: ["PS1", "Decompilation", "Preservation", "Reverse Engineering"],
            DocumentationPaths: ["docs/project_plan.md", "docs/disc_info.md", "docs/reverse_engineering_notes.md"],
            BuildCommand: null,
            RunCommand: null,
            TestCommand: "./scripts/extract_us.sh all",
            MatchingBuildCommand: null,
            SuggestedCommands: []),

        new(
            Key: "ten-revival",
            DisplayName: "TEN Revival",
            Description: "Revival of the Total Entertainment Network online service: reverse engineering the client protocol and reimplementing the lobby, matchmaking, and game relay servers so the original clients can connect again.",
            ObjectivePlaceholder: "e.g. Get a client through lobby login and into matchmaking",
            ActiveTaskPlaceholder: "e.g. Decoding the next unknown message type in a capture",
            PreferredAiProvider: "Claude (Sonnet) — protocol RE pairing",
            AiContextPaths: ["docs/protocol/message-catalog.md", "src/relay/", "include/ten_protocol.h"],
            PromptRules: "Never guess at an unknown protocol field without a capture to back it up.\n" +
                         "Keep the message catalog doc in sync with any new opcode implemented.\n" +
                         "Original client binaries and captures are reference material, never to be committed.",
            CustomLogPatterns:
            [
                new LogPatternDefinition("PROTO ERROR", @"protocol error|unknown opcode", IsRegex: true, Category: "Protocol"),
            ],
            Tags: ["Networking", "Game Preservation", "Reverse Engineering", "Server Revival"],
            DocumentationPaths: ["docs/protocol/", "docs/lobby-flow.md", "captures/README.md"],
            BuildCommand: "cmake --build build -j8",
            RunCommand: "./build/ten-server --config server.toml",
            TestCommand: "python3 tools/protocol_tests.py --captures captures/",
            MatchingBuildCommand: null,
            SuggestedCommands: [new TemplateCommand("capture-replay", "python3 tools/replay_capture.py")]),

        new(
            Key: "banshee-realm-ffxi",
            DisplayName: "Banshee Realm — FFXI Solo Server",
            Description: "Private Final Fantasy XI server configured for solo/offline play — custom drop rates, AI-scripted mobs, and zone packet handling.",
            ObjectivePlaceholder: "e.g. Get the current expansion's main mission line completable solo",
            ActiveTaskPlaceholder: "e.g. Debugging a zone crash or tuning a mob's AI script",
            PreferredAiProvider: "Claude (Sonnet) — Lua scripting pairing",
            AiContextPaths: ["scripts/globals/", "docs/packet-notes.md"],
            PromptRules: "Never commit proprietary game client assets or extracted resource files.\n" +
                         "Verify Lua script changes against a live test character before claiming a scripted event works.\n" +
                         "Solo-server balance changes (drop rates, EXP, AI) are intentional — do not 'fix' them to match retail.",
            CustomLogPatterns:
            [
                new LogPatternDefinition("LUA ERROR", @"lua.*error", IsRegex: true, Category: "ScriptError"),
                new LogPatternDefinition("MAP CRASH", "Segmentation fault", IsRegex: true, Category: "Crash"),
            ],
            Tags: ["FFXI", "Private Server", "Game Preservation", "Server Revival"],
            DocumentationPaths: ["docs/", "sql/README.md"],
            BuildCommand: "cmake --build build -j8",
            RunCommand: "./scripts/start_server.sh",
            TestCommand: null,
            MatchingBuildCommand: null,
            SuggestedCommands: [new TemplateCommand("zone-restart", "./scripts/restart_zone.sh")]),

        new(
            Key: "wotlk-solo-bots",
            DisplayName: "WoW WotLK Solo Bots",
            Description: "Private WotLK (3.3.5a) server with AI-controlled party bots, configured for solo play.",
            ObjectivePlaceholder: "e.g. Get a full 5-man bot party clearing the next dungeon",
            ActiveTaskPlaceholder: "e.g. Fixing a bot class rotation or a broken quest script",
            PreferredAiProvider: "Claude (Sonnet) — C++/SQL pairing",
            AiContextPaths: ["src/server/game/Bots/", "conf/worldserver.conf.dist"],
            PromptRules: "Never commit client-side MPQ/game data.\n" +
                         "Bot AI changes should be tested with a live party before claiming a class rotation is fixed.\n" +
                         "Config changes belong in worldserver.conf, not hardcoded — document any new conf key.",
            CustomLogPatterns:
            [
                new LogPatternDefinition("SQL ERROR", @"ERROR.*SQL", IsRegex: true, Category: "Database"),
                new LogPatternDefinition("CORE CRASH", @"Segmentation fault|Assertion.*failed", IsRegex: true, Category: "Crash"),
            ],
            Tags: ["WoW", "WotLK", "Private Server", "Server Revival"],
            DocumentationPaths: ["docs/", "src/server/scripts/README.md"],
            BuildCommand: "cmake --build build -j$(nproc)",
            RunCommand: "./worldserver",
            TestCommand: null,
            MatchingBuildCommand: null,
            SuggestedCommands: [new TemplateCommand("db-update", "./apply_db_updates.sh")]),

        new(
            Key: "generic-software",
            DisplayName: "Generic Software Project",
            Description: "An ordinary software project — Workbench does not care whether the frontier is a function address or a feature ticket.",
            ObjectivePlaceholder: "e.g. Ship the next milestone's feature set",
            ActiveTaskPlaceholder: "e.g. Working the top item on the backlog",
            PreferredAiProvider: null,
            AiContextPaths: ["docs/architecture.md"],
            PromptRules: "Follow the project's existing conventions; check docs/architecture.md before adding a new pattern.",
            CustomLogPatterns: [],
            Tags: ["Software"],
            DocumentationPaths: ["README.md", "docs/"],
            BuildCommand: "npm run build",
            RunCommand: "npm run dev",
            TestCommand: "npm test",
            MatchingBuildCommand: null,
            SuggestedCommands: []),
    ];
}

/// <summary>
/// Applies a template's suggestions onto a newly-created project — only
/// filling fields the user left empty, never overwriting anything they
/// already typed, and never touching <see cref="Project.RepositoryPath"/>
/// (a template must never assume a repository exists on this machine).
/// </summary>
public static class ProjectTemplateApplier
{
    public static void ApplyTo(ProjectTemplate template, Project project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            project.Name = template.DisplayName;

        project.Description ??= template.Description;
        project.CurrentObjective ??= template.ObjectivePlaceholder;
        project.ActiveTask ??= template.ActiveTaskPlaceholder;
        project.PreferredAiProvider ??= template.PreferredAiProvider;
        project.PromptRules ??= template.PromptRules;
        project.BuildCommand ??= template.BuildCommand;
        project.RunCommand ??= template.RunCommand;
        project.TestCommand ??= template.TestCommand;
        project.MatchingBuildCommand ??= template.MatchingBuildCommand;

        if (project.AiContextPaths.Count == 0)
            project.AiContextPaths = [.. template.AiContextPaths];
        if (project.DocumentationPaths.Count == 0)
            project.DocumentationPaths = [.. template.DocumentationPaths];
        if (project.CustomLogPatterns.Count == 0)
            project.CustomLogPatterns = [.. template.CustomLogPatterns];
        if (project.Tags.Count == 0)
            project.Tags = [.. template.Tags];
    }
}
