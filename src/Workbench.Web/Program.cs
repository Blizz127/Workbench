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

// Phase 2D mobile action layer: the allowlisted mutations. ExecutionService
// is reused as-is (history + timeline journaling included), wrapped behind a
// small interface for the predefined build/test/launch commands.
builder.Services.AddSingleton<IProjectConfigService, ProjectConfigService>();
builder.Services.AddSingleton<IBuildHistoryRepository>(sp => new BuildHistoryRepository(sp.GetRequiredService<WorkbenchDatabase>()));
builder.Services.AddSingleton<ITestHistoryRepository>(sp => new TestHistoryRepository(sp.GetRequiredService<WorkbenchDatabase>()));
builder.Services.AddSingleton<ExecutionService>();
builder.Services.AddSingleton<IPredefinedCommandRunner, PredefinedCommandRunner>();
builder.Services.AddSingleton<ConfirmationNonceService>();
builder.Services.AddSingleton<ActionJournal>();
builder.Services.AddSingleton<IMobileActionService, MobileActionService>();

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

// Pages and their JSON twins are GET. The Phase 2D action layer adds the
// host's only mutations, POST-only, each gated on session + CSRF token +
// one-time nonce before anything runs. Project routes take the database id
// via a long route constraint — never a filesystem path.
app.MapGet("/", async (DashboardSnapshotService snapshots, CancellationToken cancellationToken) =>
    Results.Content(
        DashboardPageRenderer.Render(await snapshots.BuildAsync(cancellationToken)),
        "text/html; charset=utf-8"));

app.MapGet("/projects/{id:long}", async (
    long id, DashboardSnapshotService snapshots, IMobileActionService actions,
    ActionJournal journal, CancellationToken cancellationToken) =>
    await snapshots.BuildProjectAsync(id, cancellationToken) is { } detail
        ? Results.Content(
            ProjectDetailPageRenderer.Render(detail, actions.GetActions(id), journal.Recent(id, 20)),
            "text/html; charset=utf-8")
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

// Phase 2D mobile actions. The confirm pages are GET (read-only: they only
// render a preview and issue a nonce); every state change is POST-only and
// checks the session's CSRF token before anything else runs, then consumes a
// one-time nonce bound to (project, action, step). requiresDoubleConfirmation
// actions must pass through confirm2 — the execute endpoint refuses the
// step-one nonce by purpose.
app.MapGet("/projects/{id:long}/actions/{actionId}/confirm", (
    long id, string actionId, HttpContext context,
    IMobileActionService actions, ConfirmationNonceService nonces) =>
{
    if (actions.BuildPreview(id, actionId) is not { } preview)
        return ProjectNotFound(id);

    // Blocked actions render their reason with no form — no nonce needed.
    if (preview.BlockReason is not null)
        return Html(ActionPageRenderer.RenderConfirmation(preview, string.Empty, string.Empty));

    var purpose = preview.Safety is MobileActionSafety.RequiresDoubleConfirmation
        ? ConfirmationNonceService.PurposeConfirm2
        : ConfirmationNonceService.PurposeExecute;
    var nonce = nonces.Issue(id, actionId, purpose);
    return Html(ActionPageRenderer.RenderConfirmation(preview, CsrfToken(context), nonce));
});

app.MapPost("/projects/{id:long}/actions/{actionId}/confirm2", async (
    long id, string actionId, HttpContext context,
    IMobileActionService actions, ConfirmationNonceService nonces, SessionStore sessions) =>
{
    // A non-form POST can never carry a valid CSRF token; reject it as such
    // instead of letting ReadFormAsync throw on the content type.
    var form = context.Request.HasFormContentType ? await context.Request.ReadFormAsync() : null;
    if (form is null || !CsrfValid(context, sessions, form))
        return Rejected(id, "The form's security token is missing or invalid — reload the page and try again.");
    if (!nonces.Validate(id, actionId, ConfirmationNonceService.PurposeConfirm2, form["nonce"].FirstOrDefault()))
        return Rejected(id, "This confirmation has expired or was already used — start again from the action list.");
    if (actions.BuildPreview(id, actionId) is not { } preview)
        return ProjectNotFound(id);
    if (preview.BlockReason is not null)
        return Html(ActionPageRenderer.RenderConfirmation(preview, string.Empty, string.Empty));

    var nonce = nonces.Issue(id, actionId, ConfirmationNonceService.PurposeExecute);
    return Html(ActionPageRenderer.RenderSecondConfirmation(preview, CsrfToken(context), nonce));
});

app.MapPost("/projects/{id:long}/actions/{actionId}/execute", async (
    long id, string actionId, HttpContext context,
    IMobileActionService actions, ConfirmationNonceService nonces, SessionStore sessions) =>
{
    var form = context.Request.HasFormContentType ? await context.Request.ReadFormAsync() : null;
    if (form is null || !CsrfValid(context, sessions, form))
        return Rejected(id, "The form's security token is missing or invalid — reload the page and try again.");
    if (!nonces.Validate(id, actionId, ConfirmationNonceService.PurposeExecute, form["nonce"].FirstOrDefault()))
        return Rejected(id, "This confirmation has expired or was already used — start again from the action list.");

    return await actions.ExecuteAsync(id, actionId, confirmed: true, context.RequestAborted) is { } execution
        ? Html(ActionPageRenderer.RenderResult(execution, id))
        : ProjectNotFound(id);
});

app.Run();

static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");

static IResult ProjectNotFound(long id) =>
    Results.Content(ProjectDetailPageRenderer.RenderNotFound(id), "text/html; charset=utf-8", statusCode: 404);

static IResult Rejected(long id, string reason) =>
    Results.Content(ActionPageRenderer.RenderRejection(id, reason), "text/html; charset=utf-8", statusCode: 403);

static string CsrfToken(HttpContext context) =>
    context.Items[TokenAuthMiddleware.CsrfTokenItemKey] as string ?? string.Empty;

static bool CsrfValid(HttpContext context, SessionStore sessions, IFormCollection form) =>
    sessions.ValidateCsrf(
        context.Items[TokenAuthMiddleware.SessionIdItemKey] as string,
        form["csrf"].FirstOrDefault());

/// <summary>Entry point marker so WebApplicationFactory can host the app in tests.</summary>
public partial class Program;
