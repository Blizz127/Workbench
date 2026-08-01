using Workbench.Core.Services;
using Workbench.Data;
using Workbench.Data.Repositories;
using Workbench.Git;
using Workbench.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "WORKBENCH_WEB_");

// Eager load only for the Kestrel bind (decided before Build); the DI
// registration below re-reads configuration lazily so it always sees the
// final, fully-composed configuration (including test overrides).
var bind = WebDashboardOptions.Load(builder.Configuration);
builder.WebHost.UseUrls($"http://{bind.BindAddress}:{bind.Port}");

builder.Services.AddSingleton(_ => WebDashboardOptions.Load(builder.Configuration));
builder.Services.AddSingleton<DashboardSnapshotService>();

// Shared typed services — the same implementations the desktop app uses.
// The database-backed pieces are registered lazily so the read-only web host
// never opens (or migrates) SQLite until a request actually needs it; tests
// swap these registrations for stubs without touching a real database.
builder.Services.AddSingleton(sp => new WorkbenchDatabase(
    sp.GetRequiredService<WebDashboardOptions>().DatabasePath is { Length: > 0 } databasePath
        ? databasePath
        : WorkbenchDatabase.GetDefaultDatabasePath()));
builder.Services.AddSingleton<IProjectRepository>(sp => new ProjectRepository(sp.GetRequiredService<WorkbenchDatabase>()));
builder.Services.AddSingleton<ITimelineRepository>(sp => new TimelineRepository(sp.GetRequiredService<WorkbenchDatabase>()));
builder.Services.AddSingleton<ISystemHealthService, SystemHealthService>();
builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IDockerComposeService>(sp => new DockerComposeService(sp.GetRequiredService<IProcessRunner>()));
builder.Services.AddSingleton<ITmuxService>(sp => new TmuxService(sp.GetRequiredService<IProcessRunner>()));

var app = builder.Build();

var options = app.Services.GetRequiredService<WebDashboardOptions>();
if (!options.BindAddressIsPrivate)
{
    app.Logger.LogWarning(
        "Workbench web is bound to {BindAddress} — a PUBLIC address. The dashboard shows live " +
        "development state; bind a loopback or Tailscale (100.64.0.0/10) address instead.",
        options.BindAddress);
}

app.UseMiddleware<TokenAuthMiddleware>();

// Read-only by construction: these are the only two routes, both GET.
// Mutation verbs get 405; there is nothing to POST to.
app.MapGet("/", async (DashboardSnapshotService snapshots, CancellationToken cancellationToken) =>
    Results.Content(
        DashboardPageRenderer.Render(await snapshots.BuildAsync(cancellationToken)),
        "text/html; charset=utf-8"));

app.MapGet("/api/snapshot", async (DashboardSnapshotService snapshots, CancellationToken cancellationToken) =>
    Results.Json(await snapshots.BuildAsync(cancellationToken)));

app.Run();

/// <summary>Entry point marker so WebApplicationFactory can host the app in tests.</summary>
public partial class Program;
