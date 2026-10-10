using BackendServer.Hubs;
using BackendServer.Services;
using SharedLogic;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection("Game"));
builder.Services.AddSingleton<LobbyManager>();

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

if (app.Environment.IsDevelopment())
    app.UseCors();

app.MapHub<GameHub>(HubMethods.HubPath);
app.MapGet("/", () => "Ricochet Game Server is running!");

app.Run();
