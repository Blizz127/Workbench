using Workbench.Core.Models;

namespace Workbench.Core.Services;

public interface IProjectRepository
{
    IReadOnlyList<Project> GetAll();
    Project? GetById(long id);
    long Add(Project project);
    void Update(Project project);
    void Delete(long id);
}

public interface IMilestoneRepository
{
    IReadOnlyList<Milestone> GetForProject(long projectId);
    long Add(Milestone milestone);
    void Update(Milestone milestone);
    void Delete(long id);
}

public interface IFrontierRepository
{
    IReadOnlyList<Frontier> GetForProject(long projectId);
    Frontier? GetCurrent(long projectId);
    Frontier? GetNext(long projectId);
    long Add(Frontier frontier);
    void Update(Frontier frontier);
    void Delete(long id);
}

public interface IBlockerRepository
{
    IReadOnlyList<Blocker> GetForProject(long projectId);
    IReadOnlyList<Blocker> GetOpen(long projectId);
    long Add(Blocker blocker);
    void Update(Blocker blocker);
    void Delete(long id);
}

public interface ITimelineRepository
{
    /// <summary>Newest first.</summary>
    IReadOnlyList<TimelineEntry> GetForProject(long projectId, int limit = 200);
    long Add(TimelineEntry entry);
}

public interface INoteRepository
{
    IReadOnlyList<Note> GetForProject(long projectId);
    Note? GetById(long id);
    long Add(Note note);
    void Update(Note note);
    void Delete(long id);
}

public interface IBuildHistoryRepository
{
    /// <summary>Newest first.</summary>
    IReadOnlyList<BuildRecord> GetForProject(long projectId, int limit = 100, ExecutionKind? kind = null);
    BuildRecord? GetLatest(long projectId, ExecutionKind kind = ExecutionKind.Build);
    long Add(BuildRecord record);
}

public interface ITestHistoryRepository
{
    /// <summary>Newest first.</summary>
    IReadOnlyList<TestRecord> GetForProject(long projectId, int limit = 100);
    TestRecord? GetLatest(long projectId);
    long Add(TestRecord record);
}
