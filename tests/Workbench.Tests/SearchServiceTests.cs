using Workbench.Core.Models;
using Workbench.Data;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class SearchServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public SearchServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void Search_spans_projects_notes_milestones_timeline_and_frontiers()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var id = projects.Add(new Project { Name = "Zebra Port", Tags = ["Networking"] });

        new NoteRepository(_fixture.Database)
            .Add(new Note { ProjectId = id, Title = "zebra notes", Content = "the zebra protocol handshake" });
        new MilestoneRepository(_fixture.Database)
            .Add(new Milestone { ProjectId = id, Title = "zebra lobby working" });
        new TimelineRepository(_fixture.Database)
            .Add(new TimelineEntry { ProjectId = id, Message = "implemented zebra relay" });
        new FrontierRepository(_fixture.Database)
            .Add(new Frontier { ProjectId = id, Title = "zebra packet decoder", Status = FrontierStatus.Current });

        var results = new SearchService(_fixture.Database).Search("zebra");

        Assert.Contains(results, r => r.Domain == SearchDomain.Project);
        Assert.Contains(results, r => r.Domain == SearchDomain.Note);
        Assert.Contains(results, r => r.Domain == SearchDomain.Milestone);
        Assert.Contains(results, r => r.Domain == SearchDomain.Timeline);
        Assert.Contains(results, r => r.Domain == SearchDomain.Frontier);
    }

    [Fact]
    public void Search_is_case_insensitive_and_escapes_like_wildcards()
    {
        var projects = new ProjectRepository(_fixture.Database);
        projects.Add(new Project { Name = "UPPER case project", Description = "contains 100% literal percent" });

        var search = new SearchService(_fixture.Database);

        Assert.Contains(search.Search("upper CASE"), r => r.Domain == SearchDomain.Project);
        // '%' must match literally, not as a wildcard.
        Assert.Contains(search.Search("100%"), r => r.Domain == SearchDomain.Project);
        Assert.Empty(search.Search("100%x"));
    }

    [Fact]
    public void Blank_query_returns_nothing()
    {
        var search = new SearchService(_fixture.Database);
        Assert.Empty(search.Search("   "));
    }
}
