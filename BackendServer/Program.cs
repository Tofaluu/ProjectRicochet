using BackendServer.Hubs;
using BackendServer.Services;
using Microsoft.EntityFrameworkCore;
using SharedLogic;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection("Game"));
builder.Services.Configure<DailyOptions>(builder.Configuration.GetSection("Daily"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LobbyManager>();

// Daily puzzle results live in PostgreSQL (connection string "Daily", from the server's environment, never the
// repo). Without one (local runs) they're kept in memory and lost on restart.
string? dailyDatabase = builder.Configuration.GetConnectionString("Daily");
if (!string.IsNullOrWhiteSpace(dailyDatabase))
{
    builder.Services.AddDbContextFactory<DailyDbContext>(options => options.UseNpgsql(dailyDatabase));
    builder.Services.AddSingleton<IDailyStore, EfDailyStore>();
}
else
{
    builder.Services.AddSingleton<IDailyStore, InMemoryDailyStore>();
}
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

app.Run();
