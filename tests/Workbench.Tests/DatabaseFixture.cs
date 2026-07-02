using Workbench.Data;

namespace Workbench.Tests;

/// <summary>Creates an isolated on-disk SQLite database per test class.</summary>
public sealed class DatabaseFixture : IDisposable
{
    public WorkbenchDatabase Database { get; }

    private readonly string _directory;

    public DatabaseFixture()
    {
        _directory = Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));
        Database = new WorkbenchDatabase(Path.Combine(_directory, "test.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
