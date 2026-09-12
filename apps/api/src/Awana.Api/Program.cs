var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

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
