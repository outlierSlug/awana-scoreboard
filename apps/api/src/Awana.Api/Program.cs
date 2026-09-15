using Awana.Api.Auth;
using Awana.Api.Endpoints;
using Awana.Api.Realtime;
using Awana.Api.Services;
using Awana.Data;
using Awana.Scoring;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Database, migrations and reference-data seeding. Registration lives in
// Awana.Data so the EF and Npgsql packages stay behind that boundary.
builder.Services.AddAwanaData(builder.Configuration);

// The scoring engine holds no state, so one instance serves everything.
builder.Services.AddSingleton<IScoringEngine, ScoringEngine>();

builder.Services.AddScoped<ScoreboardService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<RoundService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<ScoringProfileService>();
builder.Services.AddSingleton<IScoreboardBroadcaster, ScoreboardBroadcaster>();

// Google sign-in onto a cookie session, plus the role policies. Registered
// before CORS matters, because the cookie only reaches the API if both agree.
builder.Services.AddAwanaAuth(builder.Configuration);

builder.Services.AddSignalR();

// RFC 9457 problem responses for anything unhandled, so a failure never
// reaches a client as a stack trace or an empty 500.
builder.Services.AddProblemDetails();

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

// Render terminates TLS at its proxy and forwards plain HTTP to the container,
// so without this the app believes every request arrived over http.
//
// That is not a cosmetic difference. The Google handler builds its redirect_uri
// from the current request's scheme and host, so it would send Google
// "http://api.awanascoreboard.org/signin-google" while the console has the
// https form registered, and sign-in fails with redirect_uri_mismatch and
// nothing in the API's own logs to explain it. The same mistake stops
// SameAsRequest from marking the session cookie Secure.
//
// Placed first: everything after it, CORS and authentication included, reads
// the scheme this corrects.
if (!string.IsNullOrWhiteSpace(port))
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    };

    // The defaults only trust loopback, and Render's proxy is neither loopback
    // nor an address that can be known in advance. Clearing both is what makes
    // the headers above take effect at all.
    //
    // Safe here only because this branch requires $PORT, which is set by the
    // platform: the app is reachable solely through that proxy, so nothing else
    // is in a position to forge these headers. It must NOT become unconditional,
    // since a directly reachable server would then let any caller claim any
    // scheme and host it liked.
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();

    app.UseForwardedHeaders(forwarded);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors(WebCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// Render polls this to decide whether a deploy succeeded and whether the
// instance is still alive. It must stay cheap and must not touch the database,
// otherwise a slow query turns into a restart loop.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "awana-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapAwanaApi();

// The board and the console join the same group, so they cannot disagree, and
// a second device can be opened mid-session with no handover step.
app.MapHub<ScoreboardHub>("/hubs/scoreboard");

app.Run();

// Exposed so the integration tests can boot this exact application rather than
// a rebuilt approximation of it.
public partial class Program;
