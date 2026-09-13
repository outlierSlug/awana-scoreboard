using Awana.Data;

var builder = WebApplication.CreateBuilder(args);

// Database, migrations and reference-data seeding. Registration lives in
// Awana.Data so the EF and Npgsql packages stay behind that boundary.
builder.Services.AddAwanaData(builder.Configuration);

// Render assigns the port at runtime and passes it as $PORT.
//
// This has to happen in code rather than in the Dockerfile. The obvious
// Dockerfile version, ENV ASPNETCORE_URLS=http://+:${PORT}, does not expand the
// variable in an exec-form ENTRYPOINT, so the app binds to the literal string,
// Render sees nothing listening, and the deploy fails with "no open ports
// detected" and no other clue.
//
// Locally $PORT is unset and the URL from launchSettings.json is used instead.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// The browser app is served from a different origin than the API, in every
// environment, so CORS is always required.
//
// The origin list is configuration, never a wildcard. AllowAnyOrigin cannot be
// combined with AllowCredentials, and a wildcard plus credentials is the
// standard way to accidentally expose an authenticated API to any site the
// user happens to be visiting.
const string WebCorsPolicy = "web";

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(WebCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Needed so the browser will send the auth cookie on API calls.
        .AllowCredentials());
});

var app = builder.Build();

app.UseCors(WebCorsPolicy);

// Render polls this to decide whether a deploy succeeded and whether the
// instance is still alive. It must stay cheap and must not touch the database,
// otherwise a slow query turns into a restart loop.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "awana-api",
    utc = DateTimeOffset.UtcNow
}));

app.Run();
