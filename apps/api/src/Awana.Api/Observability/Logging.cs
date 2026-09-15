using System.Diagnostics;
using Awana.Api.Auth;

namespace Awana.Api.Observability;

/// <summary>
/// Logs meant to be searched at 7pm on a Friday, not read afterwards.
///
/// Deliberately built on what the framework already has rather than on a
/// logging library. Every call site in this codebase already writes message
/// templates with named holes, so the structure exists; what was missing was an
/// output format that kept the holes as fields instead of flattening them into
/// a sentence.
/// </summary>
public static class Logging
{
    /// <summary>Anything slower than this is worth noticing on a games night.</summary>
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(1);

    public static ILoggingBuilder AddAwanaLogging(
        this ILoggingBuilder logging, IHostEnvironment environment)
    {
        // TraceId on every line, which is what ties a handful of lines back to
        // one request. It is also the id already handed to the browser in a
        // ProblemDetails response, so somebody reporting "it said traceId
        // 00-abc..." can be answered by searching for exactly that.
        logging.Configure(options =>
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        // JSON only where something is going to parse it. Locally the default
        // console stays, because JSON in a terminal is worse than useless when
        // the thing being debugged is a round that scored wrong.
        if (!environment.IsDevelopment())
        {
            logging.ClearProviders();
            logging.AddJsonConsole(options =>
            {
                // Without this the TraceId above is tracked and then dropped.
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            });
        }

        return logging;
    }

    /// <summary>
    /// One line per request: what was asked for, what came back, how long it
    /// took, and who was signed in.
    /// </summary>
    public static WebApplication UseRequestLogging(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Awana.Api.Requests");

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;

            // Render polls health every few seconds, forever. Logging it buries
            // the night's actual traffic under thousands of identical lines,
            // and a health check that fails is already visible in Render's own
            // dashboard.
            //
            // Hub connections are skipped for the opposite reason: a WebSocket
            // lives for the whole session, so its "duration" would be the
            // evening and the line would not arrive until it was over.
            if (path.StartsWithSegments("/api/health") || path.StartsWithSegments("/hubs"))
            {
                await next();
                return;
            }

            var started = Stopwatch.GetTimestamp();

            try
            {
                await next();
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                var status = context.Response.StatusCode;

                // The user id, never the address. It is enough to tell two
                // volunteers apart on a shared laptop, and an email in a log is
                // a copy of somebody's personal data sitting somewhere nobody
                // is thinking about.
                var user = context.User.Id();

                var level = status >= 500 ? LogLevel.Error
                    : status >= 400 ? LogLevel.Warning
                    : elapsed > Slow ? LogLevel.Warning
                    : LogLevel.Information;

                logger.Log(
                    level,
                    "{Method} {Path} responded {StatusCode} in {ElapsedMs}ms for user {UserId}",
                    context.Request.Method,
                    path.Value,
                    status,
                    (int)elapsed.TotalMilliseconds,
                    user);
            }
        });

        return app;
    }
}
