using Workbench.Core.Models;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class BlockerRepositoryTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly BlockerRepository _blockers;
    private readonly long _projectId;

    public BlockerRepositoryTests()
    {
        _blockers = new BlockerRepository(_fixture.Database);
        _projectId = new ProjectRepository(_fixture.Database).Add(new Project { Name = "Blocker Repo" });
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Add_generates_a_stable_id_and_preserves_a_given_one()
    {
        var generated = new Blocker { ProjectId = _projectId, Description = "no id given" };
        _blockers.Add(generated);
        Assert.False(string.IsNullOrEmpty(generated.StableId));

        var given = new Blocker { ProjectId = _projectId, Description = "id given", StableId = "from-shared-file" };
        _blockers.Add(given);
        Assert.Equal("from-shared-file", given.StableId);

        var loaded = _blockers.GetForProject(_projectId);
        Assert.Equal(2, loaded.Select(b => b.StableId).Distinct().Count());
    }

    [Fact]
    public void Rich_fields_round_trip_through_the_repository()
    {
        var blocker = new Blocker
        {
            ProjectId = _projectId,
            Title = "Camera Y pitch chain broken",
            Description = "Long form description of the camera blocker",
            BlockerClass = "camera math",
            Severity = "critical",
            Status = BlockerStatus.Monitoring,
            Evidence = "captures/render_diag/permfix_01.log",
            NextSafestStep = "Read-only pass over func_8007CD80",
            Owner = "blizz",
            Tags = ["PSX", "camera"],
            SourceReference = "upstream issue #42",
        };
        _blockers.Add(blocker);

        var loaded = _blockers.GetForProject(_projectId).Single();
        Assert.Equal(blocker.StableId, loaded.StableId);
        Assert.Equal("Camera Y pitch chain broken", loaded.Title);
        Assert.Equal("camera math", loaded.BlockerClass);
        Assert.Equal("critical", loaded.Severity);
        Assert.Equal(BlockerStatus.Monitoring, loaded.Status);
        Assert.Equal("captures/render_diag/permfix_01.log", loaded.Evidence);
        Assert.Equal("Read-only pass over func_8007CD80", loaded.NextSafestStep);
        Assert.Equal("blizz", loaded.Owner);
        Assert.Equal(["PSX", "camera"], loaded.Tags);
        Assert.Equal("upstream issue #42", loaded.SourceReference);
        Assert.False(loaded.IsResolved); // Monitoring is still open
        Assert.Equal(loaded.CreatedAt, loaded.UpdatedAt);
    }

    [Fact]
    public void Is_resolved_stays_in_sync_with_status_on_every_write()
    {
        // Setting the legacy flag only → status follows.
        var blocker = new Blocker
        {
            ProjectId = _projectId,
            Description = "resolved via flag",
            IsResolved = true,
            ResolvedAt = DateTimeOffset.Now,
        };
        _blockers.Add(blocker);
        Assert.Equal(BlockerStatus.Resolved, blocker.Status);

        // Setting the status → the flag follows (Dismissed counts as resolved).
        blocker.Status = BlockerStatus.Dismissed;
        _blockers.Update(blocker);
        Assert.True(_blockers.GetForProject(_projectId).Single().IsResolved);

        // Reopening clears the flag and the resolved timestamp.
        blocker.Status = BlockerStatus.Active;
        blocker.ResolvedAt = null;
        _blockers.Update(blocker);
        var loaded = _blockers.GetForProject(_projectId).Single();
        Assert.False(loaded.IsResolved);
        Assert.Null(loaded.ResolvedAt);
        Assert.DoesNotContain(_blockers.GetOpen(_projectId), b => b.IsResolved);
    }

    [Fact]
    public void Update_bumps_updated_at()
    {
        var blocker = new Blocker
        {
            ProjectId = _projectId,
            Description = "old timestamp",
            CreatedAt = DateTimeOffset.Now.AddDays(-3),
            UpdatedAt = DateTimeOffset.Now.AddDays(-3),
        };
        _blockers.Add(blocker);
        var before = _blockers.GetForProject(_projectId).Single().UpdatedAt;

        blocker.Description = "edited";
        _blockers.Update(blocker);

        var after = _blockers.GetForProject(_projectId).Single().UpdatedAt;
        Assert.True(after > before, $"expected updated_at to move forward ({before:O} → {after:O})");
    }
}
