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
builder.Services.AddSingleton<SessionStore>();
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
builder.Services.AddSingleton<IProjectFileService, ProjectFileService>();
builder.Services.AddSingleton<ProjectCodeService>();
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

// Read-only by construction: every route is a GET. Mutation verbs get 405;
// there is nothing to POST to. Project routes take the database id via a long
// route constraint — never a filesystem path.
app.MapGet("/", async (DashboardSnapshotService snapshots, CancellationToken cancellationToken) =>
    Results.Content(
        DashboardPageRenderer.Render(await snapshots.BuildAsync(cancellationToken)),
        "text/html; charset=utf-8"));

app.MapGet("/projects/{id:long}", async (long id, DashboardSnapshotService snapshots, CancellationToken cancellationToken) =>
    await snapshots.BuildProjectAsync(id, cancellationToken) is { } detail
        ? Results.Content(ProjectDetailPageRenderer.Render(detail), "text/html; charset=utf-8")
        : Results.Content(
            ProjectDetailPageRenderer.RenderNotFound(id), "text/html; charset=utf-8", statusCode: 404));

app.MapGet("/api/snapshot", async (DashboardSnapshotService snapshots, CancellationToken cancellationToken) =>
    Results.Json(await snapshots.BuildAsync(cancellationToken)));

app.MapGet("/api/projects/{id:long}", async (long id, DashboardSnapshotService snapshots, CancellationToken cancellationToken) =>
    await snapshots.BuildProjectAsync(id, cancellationToken) is { } detail
        ? Results.Json(detail)
        : Results.NotFound());

// Phase 2C code browsing — same read-only shape as the rest of the host: GET
// only, ids via long constraint, paths confined to the project's repository
// root by ProjectFileService. These pages are static (no polling endpoint);
// unknown project ids get the same styled 404 as the detail page.
app.MapGet("/projects/{id:long}/files", (long id, string? path, ProjectCodeService code) =>
    code.BuildDirectoryListing(id, path) is { } snapshot
        ? Results.Content(CodePageRenderer.RenderDirectory(snapshot), "text/html; charset=utf-8")
        : Results.Content(ProjectDetailPageRenderer.RenderNotFound(id), "text/html; charset=utf-8", statusCode: 404));

app.MapGet("/projects/{id:long}/file", (long id, string? path, int? line, string? sha, ProjectCodeService code) =>
    code.BuildFileView(id, path, sha) is { } snapshot
        ? Results.Content(CodePageRenderer.RenderFile(snapshot), "text/html; charset=utf-8")
        : Results.Content(ProjectDetailPageRenderer.RenderNotFound(id), "text/html; charset=utf-8", statusCode: 404));

app.MapGet("/projects/{id:long}/search", (long id, string? q, string? kind, ProjectCodeService code) =>
    code.BuildSearch(id, q, kind) is { } snapshot
        ? Results.Content(CodePageRenderer.RenderSearch(snapshot), "text/html; charset=utf-8")
        : Results.Content(ProjectDetailPageRenderer.RenderNotFound(id), "text/html; charset=utf-8", statusCode: 404));

app.MapGet("/projects/{id:long}/changes", (long id, ProjectCodeService code) =>
    code.BuildChanges(id) is { } snapshot
        ? Results.Content(CodePageRenderer.RenderChanges(snapshot), "text/html; charset=utf-8")
        : Results.Content(ProjectDetailPageRenderer.RenderNotFound(id), "text/html; charset=utf-8", statusCode: 404));

app.MapGet("/projects/{id:long}/commits/{sha}", (long id, string sha, ProjectCodeService code) =>
    code.BuildCommit(id, sha) is { } snapshot
        ? Results.Content(CodePageRenderer.RenderCommit(snapshot), "text/html; charset=utf-8")
        : Results.Content(ProjectDetailPageRenderer.RenderNotFound(id), "text/html; charset=utf-8", statusCode: 404));

app.Run();

/// <summary>Entry point marker so WebApplicationFactory can host the app in tests.</summary>
public partial class Program;
