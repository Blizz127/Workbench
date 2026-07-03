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
    /// <summary>
    /// Newest first. Records returned by list queries have an empty
    /// <see cref="BuildRecord.OutputLog"/> — logs can be large, so they are
    /// fetched individually via <see cref="GetOutputLog"/>.
    /// </summary>
    IReadOnlyList<BuildRecord> GetForProject(long projectId, int limit = 100, ExecutionKind? kind = null);

    /// <summary>Latest record without its output log (see <see cref="GetForProject"/>).</summary>
    BuildRecord? GetLatest(long projectId, ExecutionKind kind = ExecutionKind.Build);

    /// <summary>Full output log for one record, or null if the record does not exist.</summary>
    string? GetOutputLog(long recordId);

    long Add(BuildRecord record);
}

public interface ITestHistoryRepository
{
    /// <summary>
    /// Newest first. Records returned by list queries have an empty
    /// <see cref="TestRecord.OutputLog"/> — logs can be large, so they are
    /// fetched individually via <see cref="GetOutputLog"/>.
    /// </summary>
    IReadOnlyList<TestRecord> GetForProject(long projectId, int limit = 100);

    /// <summary>Latest record without its output log (see <see cref="GetForProject"/>).</summary>
    TestRecord? GetLatest(long projectId);

    /// <summary>Full output log for one record, or null if the record does not exist.</summary>
    string? GetOutputLog(long recordId);

    long Add(TestRecord record);
}

public interface IProjectCommandRepository
{
    IReadOnlyList<ProjectCommand> GetForProject(long projectId);
    long Add(ProjectCommand command);
    void Update(ProjectCommand command);
    void Delete(long id);
}

public interface ICommandRunRepository
{
    /// <summary>
    /// Newest first. Records returned by list queries have an empty
    /// <see cref="CommandRun.OutputLog"/> — logs can be large, so they are
    /// fetched individually via <see cref="GetOutputLog"/>.
    /// </summary>
    IReadOnlyList<CommandRun> GetForProject(long projectId, int limit = 100, string? commandName = null);

    /// <summary>Latest run of the given command name, without its output log.</summary>
    CommandRun? GetLatest(long projectId, string commandName);

    /// <summary>Full output log for one record, or null if the record does not exist.</summary>
    string? GetOutputLog(long recordId);

    long Add(CommandRun record);
}
