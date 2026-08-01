using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class ComposeCommandBuilderTests
{
    private const string ComposeFile = "/repo/docker-compose.yml";

    [Fact]
    public void Inspect_command_is_read_only_and_names_the_file()
    {
        Assert.Equal(
            "docker compose -f \"/repo/docker-compose.yml\" ps --all --format json",
            ComposeCommandBuilder.InspectCommand(ComposeFile));
    }

    [Fact]
    public void Project_wide_commands_are_built_exactly()
    {
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" up -d",
            ComposeCommandBuilder.Build(ComposeOperationKind.StartAll, ComposeFile).Command);
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" stop",
            ComposeCommandBuilder.Build(ComposeOperationKind.StopAll, ComposeFile).Command);
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" restart",
            ComposeCommandBuilder.Build(ComposeOperationKind.RestartAll, ComposeFile).Command);
    }

    [Fact]
    public void Service_commands_target_exactly_one_service()
    {
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" start map",
            ComposeCommandBuilder.Build(ComposeOperationKind.StartService, ComposeFile, "map").Command);
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" stop map",
            ComposeCommandBuilder.Build(ComposeOperationKind.StopService, ComposeFile, "map").Command);
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" restart map",
            ComposeCommandBuilder.Build(ComposeOperationKind.RestartService, ComposeFile, "map").Command);
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" logs --no-color --tail=200 map",
            ComposeCommandBuilder.Build(ComposeOperationKind.LogsService, ComposeFile, "map").Command);
    }

    [Fact]
    public void Whole_project_logs_command_has_no_service_argument()
    {
        Assert.Equal("docker compose -f \"/repo/docker-compose.yml\" logs --no-color --tail=200",
            ComposeCommandBuilder.Build(ComposeOperationKind.LogsAll, ComposeFile).Command);
    }

    [Fact]
    public void Stop_and_restart_require_confirmation_start_and_logs_do_not()
    {
        Assert.True(ComposeCommandBuilder.Build(ComposeOperationKind.StopAll, ComposeFile).RequiresConfirmation);
        Assert.True(ComposeCommandBuilder.Build(ComposeOperationKind.RestartAll, ComposeFile).RequiresConfirmation);
        Assert.True(ComposeCommandBuilder.Build(ComposeOperationKind.StopService, ComposeFile, "map").RequiresConfirmation);
        Assert.True(ComposeCommandBuilder.Build(ComposeOperationKind.RestartService, ComposeFile, "map").RequiresConfirmation);

        Assert.False(ComposeCommandBuilder.Build(ComposeOperationKind.StartAll, ComposeFile).RequiresConfirmation);
        Assert.False(ComposeCommandBuilder.Build(ComposeOperationKind.StartService, ComposeFile, "map").RequiresConfirmation);
        Assert.False(ComposeCommandBuilder.Build(ComposeOperationKind.LogsAll, ComposeFile).RequiresConfirmation);
        Assert.False(ComposeCommandBuilder.Build(ComposeOperationKind.LogsService, ComposeFile, "map").RequiresConfirmation);
    }

    [Fact]
    public void Generated_commands_never_carry_destructive_primitives()
    {
        // The whole point of the builder: stopping must preserve containers,
        // images, and volumes — no down, no -v/--volumes, no prune, no rm/rmi.
        string[] forbidden = ["down", "prune", " rm ", "rmi", " -v", "--volumes", "--remove-orphans", "--force"];

        var operations = new[]
        {
            ComposeCommandBuilder.Build(ComposeOperationKind.StartAll, ComposeFile),
            ComposeCommandBuilder.Build(ComposeOperationKind.StopAll, ComposeFile),
            ComposeCommandBuilder.Build(ComposeOperationKind.RestartAll, ComposeFile),
            ComposeCommandBuilder.Build(ComposeOperationKind.StartService, ComposeFile, "db-update"),
            ComposeCommandBuilder.Build(ComposeOperationKind.StopService, ComposeFile, "db-update"),
            ComposeCommandBuilder.Build(ComposeOperationKind.RestartService, ComposeFile, "db-update"),
            ComposeCommandBuilder.Build(ComposeOperationKind.LogsAll, ComposeFile),
            ComposeCommandBuilder.Build(ComposeOperationKind.LogsService, ComposeFile, "db-update"),
        };

        foreach (var operation in operations)
        foreach (var token in forbidden)
            Assert.DoesNotContain(token, operation.Command);
    }

    [Fact]
    public void Service_operations_reject_missing_or_shell_smuggled_names()
    {
        Assert.Throws<ArgumentException>(() =>
            ComposeCommandBuilder.Build(ComposeOperationKind.StopService, ComposeFile));
        Assert.Throws<ArgumentException>(() =>
            ComposeCommandBuilder.Build(ComposeOperationKind.StopService, ComposeFile, "map; rm -rf /"));
        Assert.Throws<ArgumentException>(() =>
            ComposeCommandBuilder.Build(ComposeOperationKind.RestartService, ComposeFile, "map$(reboot)"));
        Assert.Throws<ArgumentException>(() =>
            ComposeCommandBuilder.Build(ComposeOperationKind.LogsService, ComposeFile, "map --follow"));
    }

    [Fact]
    public void Service_names_with_compose_legal_punctuation_are_accepted()
    {
        var operation = ComposeCommandBuilder.Build(ComposeOperationKind.StopService, ComposeFile, "db-update_2.1");
        Assert.Equal("db-update_2.1", operation.ServiceName);
    }
}
