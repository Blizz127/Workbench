using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class ComposePsParserTests
{
    private const string ComposeFile = "/repo/docker-compose.yml";

    private static string Line(string service, string state, int? exitCode = 0,
        string health = "", string status = "", string? project = "proj", string? name = null) =>
        $$"""{"Name":"{{name ?? $"proj-{service}-1"}}","Service":"{{service}}","State":"{{state}}","Status":"{{status}}","ExitCode":{{exitCode?.ToString() ?? "null"}},"Health":"{{health}}","Project":"{{project}}"}""";

    [Fact]
    public void Real_banshee_snapshot_parses_into_six_typed_services()
    {
        // Captured from `docker compose ps --all --format json` on the live
        // Banshee Realm 11 stack (fields trimmed).
        var output = string.Join('\n',
            """{"Name":"banshee-realm-11-connect-1","Service":"connect","State":"running","Status":"Up 3 hours","ExitCode":0,"Health":"","Project":"banshee-realm-11"}""",
            """{"Name":"banshee-realm-11-database-1","Service":"database","State":"running","Status":"Up 4 hours (healthy)","ExitCode":0,"Health":"healthy","Project":"banshee-realm-11"}""",
            """{"Name":"banshee-realm-11-db-update-1","Service":"db-update","State":"exited","Status":"Exited (0) 4 hours ago","ExitCode":0,"Health":"","Project":"banshee-realm-11"}""",
            """{"Name":"banshee-realm-11-map-1","Service":"map","State":"running","Status":"Up About an hour","ExitCode":0,"Health":"","Project":"banshee-realm-11"}""",
            """{"Name":"banshee-realm-11-search-1","Service":"search","State":"running","Status":"Up 4 hours","ExitCode":0,"Health":"","Project":"banshee-realm-11"}""",
            """{"Name":"banshee-realm-11-world-1","Service":"world","State":"running","Status":"Up 4 hours","ExitCode":0,"Health":"","Project":"banshee-realm-11"}""");

        var status = ComposePsParser.Parse(output, ComposeFile);

        Assert.Equal("banshee-realm-11", status.ProjectName);
        Assert.Equal(6, status.TotalCount);
        Assert.Equal(5, status.RunningCount);
        Assert.Equal(1, status.CompletedCount);
        Assert.Equal("5/6 running · 1 completed", status.Summary);

        var database = Assert.Single(status.Services, s => s.Name == "database");
        Assert.Equal(ComposeServiceState.Running, database.State);
        Assert.Equal(ComposeHealth.Healthy, database.Health);

        var dbUpdate = Assert.Single(status.Services, s => s.Name == "db-update");
        Assert.Equal(ComposeServiceState.Completed, dbUpdate.State);
        Assert.False(dbUpdate.IsRunning);

        foreach (var name in new[] { "connect", "map", "search", "world" })
            Assert.Equal(ComposeServiceState.Running, Assert.Single(status.Services, s => s.Name == name).State);
    }

    [Fact]
    public void One_shot_service_exited_with_code_zero_is_completed_not_failed()
    {
        var status = ComposePsParser.Parse(
            Line("db-update", "exited", exitCode: 0, status: "Exited (0) 2 minutes ago"), ComposeFile);

        var service = Assert.Single(status.Services);
        Assert.Equal(ComposeServiceState.Completed, service.State);
        Assert.Equal(0, service.ExitCode);
    }

    [Fact]
    public void Service_exited_with_a_nonzero_code_is_failed()
    {
        var status = ComposePsParser.Parse(
            Line("map", "exited", exitCode: 137, status: "Exited (137) 1 minute ago"), ComposeFile);

        var service = Assert.Single(status.Services);
        Assert.Equal(ComposeServiceState.Failed, service.State);
        Assert.Equal(137, service.ExitCode);
    }

    [Fact]
    public void Running_stopped_healthy_unhealthy_and_restarting_states_map()
    {
        var output = string.Join('\n',
            Line("a", "running", health: "healthy"),
            Line("b", "running", health: "unhealthy"),
            Line("c", "running", health: "starting"),
            Line("d", "exited", exitCode: 1),
            Line("e", "restarting", exitCode: null),
            Line("f", "created", exitCode: null),
            Line("g", "paused"),
            Line("h", "dead", exitCode: null));

        var status = ComposePsParser.Parse(output, ComposeFile);
        Assert.Equal(ComposeServiceState.Running, Assert.Single(status.Services, s => s.Name == "a").State);
        Assert.Equal(ComposeHealth.Healthy, Assert.Single(status.Services, s => s.Name == "a").Health);
        Assert.Equal(ComposeHealth.Unhealthy, Assert.Single(status.Services, s => s.Name == "b").Health);
        Assert.Equal(ComposeHealth.Starting, Assert.Single(status.Services, s => s.Name == "c").Health);
        Assert.Equal(ComposeServiceState.Failed, Assert.Single(status.Services, s => s.Name == "d").State);
        Assert.Equal(ComposeServiceState.Restarting, Assert.Single(status.Services, s => s.Name == "e").State);
        Assert.Equal(ComposeServiceState.Created, Assert.Single(status.Services, s => s.Name == "f").State);
        Assert.Equal(ComposeServiceState.Paused, Assert.Single(status.Services, s => s.Name == "g").State);
        Assert.Equal(ComposeServiceState.Failed, Assert.Single(status.Services, s => s.Name == "h").State);
    }

    [Fact]
    public void Replicated_service_rolls_up_to_the_worst_state()
    {
        var output = string.Join('\n',
            Line("web", "running", name: "proj-web-1"),
            Line("web", "exited", exitCode: 1, name: "proj-web-2"));

        var status = ComposePsParser.Parse(output, ComposeFile);

        var web = Assert.Single(status.Services);
        Assert.Equal("web", web.Name);
        Assert.Equal(ComposeServiceState.Failed, web.State);
    }

    [Fact]
    public void Non_json_noise_lines_are_skipped()
    {
        var output = string.Join('\n',
            "time=\"2026-08-01\" level=warning msg=\"some docker warning\"",
            "",
            Line("map", "running"),
            "not json at all");

        var status = ComposePsParser.Parse(output, ComposeFile);
        Assert.Equal("map", Assert.Single(status.Services).Name);
    }

    [Fact]
    public void Empty_output_means_no_containers()
    {
        var status = ComposePsParser.Parse(string.Empty, ComposeFile);
        Assert.Null(status.ProjectName);
        Assert.Empty(status.Services);
        Assert.Equal("no containers", status.Summary);
    }
}
