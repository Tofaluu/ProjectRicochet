using BackendServer.Hubs;
using BackendServer.Services;
using Microsoft.EntityFrameworkCore;
using SharedLogic;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection("Game"));
builder.Services.Configure<DailyOptions>(builder.Configuration.GetSection("Daily"));
builder.Services.Configure<UsageOptions>(builder.Configuration.GetSection("Usage"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LobbyManager>();

// Daily puzzle results live in PostgreSQL (connection string "Daily", from the server's environment, never the
// repo). Without one (local runs) they're kept in memory and lost on restart.
string? dailyDatabase = builder.Configuration.GetConnectionString("Daily");
if (!string.IsNullOrWhiteSpace(dailyDatabase))
{
    builder.Services.AddDbContextFactory<DailyDbContext>(options => options.UseNpgsql(dailyDatabase));
    builder.Services.AddSingleton<IDailyStore, EfDailyStore>();
    builder.Services.AddSingleton<IUsageStore, EfUsageStore>();
}
else
{
    builder.Services.AddSingleton<IDailyStore, InMemoryDailyStore>();
    builder.Services.AddSingleton<IUsageStore, InMemoryUsageStore>();
}
builder.Services.AddSingleton<UsageService>();
builder.Services.AddSingleton<DailyService>();
builder.Services.AddHostedService<DailyPrepareService>();

// Only browser clients (e.g. a future WebGL build) need CORS; the Unity desktop client doesn't.
// Wide open for local development only.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .SetIsOriginAllowed(_ => true)
        .AllowCredentials()));
}

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(dailyDatabase))
{
    // Bring the database's tables up to date (EF Core migrations in BackendServer/Migrations).
    await using DailyDbContext db = await app.Services.GetRequiredService<IDbContextFactory<DailyDbContext>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
}
else
{
    app.Logger.LogWarning("No \"Daily\" connection string: daily puzzle results are kept in memory only");
}

if (app.Environment.IsDevelopment())
    app.UseCors();

app.MapHub<GameHub>(HubMethods.HubPath);
app.MapGet("/", () => "Ricochet Game Server is running!");

// Usage tracking (Services/Usage): the game reports events; the stats page needs the key from the server's environment.
app.MapPost("/api/events", async (UsageEventRequest request, UsageService usage) =>
    await usage.RecordAsync(request) ? Results.NoContent() : Results.BadRequest());
app.MapPost("/api/online", async (OnlinePingRequest request, UsageService usage) =>
    await usage.PingAsync(request) ? Results.NoContent() : Results.BadRequest());
app.MapGet("/api/stats", async (string? key, UsageService usage) =>
    usage.StatsKeyMatches(key) ? Results.Content(UsagePage.Render(await usage.SummaryAsync()), "text/html") : Results.NotFound());

app.Run();
