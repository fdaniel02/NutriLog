using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NutriLog;
using NutriLog.Components;
using NutriLog.Data;
using NutriLog.Features.Barcode;
using NutriLog.Features.Diary;
using NutriLog.Features.Foods;
using NutriLog.Features.Recipes;
using NutriLog.Features.Sources;
using NutriLog.Features.Targets;
using NutriLog.Import;

// An import's arguments aren't settings; a path starting with "/" would otherwise be read as one.
var builder = WebApplication.CreateBuilder(ImportCommand.IsImport(args) ? [] : args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<AppDbContext>((services, o) =>
    o.UseAppDatabase(services.GetRequiredService<IConfiguration>(), retryOnFailure: true));
// Without EF's retries: StartupRetry already retries the migration, and the two loops would multiply.
builder.Services.AddKeyedSingleton<IDbContextFactory<AppDbContext>>(DatabaseMigrator.DbFactoryKey, (services, _) =>
    new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
        .UseAppDatabase(services.GetRequiredService<IConfiguration>(), retryOnFailure: false).Options));

// In the database, not the container: keys lost on a restart make every browser's antiforgery cookie undecryptable.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("NutriLog");

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The proxy (Traefik) has no fixed IP, and the pod is only reachable through the cluster network.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddAppTelemetry(builder.Configuration);
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services =>
{
    var timeZoneId = builder.Configuration["TimeZone"]
        ?? throw new InvalidOperationException("Setting 'TimeZone' is missing (an IANA id, e.g. 'Europe/Amsterdam').");
    return new Clock(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId), services.GetRequiredService<TimeProvider>());
});
builder.Services.AddTransient<IDiary, Diary>();
builder.Services.AddTransient<IFoods, Foods>();
builder.Services.AddTransient<IRecipes, Recipes>();
builder.Services.AddTransient<ISourceQueries, SourceQueries>();
builder.Services.AddHttpClient<IRunStats, RunStatsClient>(client =>
{
    client.BaseAddress = RunStatsClient.BaseAddress(builder.Configuration["RunStats:BaseUrl"]);
    client.Timeout = RunStatsClient.Timeout;
});
builder.Services.AddTransient<ITargets, NutriLog.Features.Targets.Targets>();
builder.Services.AddHttpClient<IOpenFoodFacts, OpenFoodFacts>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["OpenFoodFacts:BaseUrl"]
        ?? throw new InvalidOperationException("Setting 'OpenFoodFacts:BaseUrl' is missing."));
    http.DefaultRequestHeaders.UserAgent.ParseAdd(OpenFoodFacts.UserAgent);
    // A scan waits for it; past this, typing the label in is quicker.
    http.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddTransient<IBarcodeLookup, BarcodeLookup>();
builder.Services.AddTransient<FoodImporter>();
builder.Services.AddTransient<ImportCommand>();

// Disposing the app on exit flushes the console logger's queue, so the last error is written before the process ends.
await using var app = builder.Build();

try
{
    await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync();

    if (ImportCommand.IsImport(args))
        return await app.Services.GetRequiredService<ImportCommand>().RunAsync(args);

    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    app.UseAntiforgery();

    // Liveness without the database check: a database outage must not make Kubernetes restart the pod.
    app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/healthz/ready");

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    await app.RunAsync();
    return 0;
}
// dotnet ef and WebApplicationFactory stop the app with HostAbortedException; it isn't an error.
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Logged here, not left to the runtime: an unhandled exception goes to stderr as multi-line text, not JSON.
    app.Logger.ApplicationFailed(ex);
    return 1;
}
