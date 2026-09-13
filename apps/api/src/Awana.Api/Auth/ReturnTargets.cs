namespace Awana.Api.Auth;

/// <summary>
/// Where the browser is sent back to after a sign-in attempt.
///
/// The API and the web app are different origins, so every one of these is an
/// absolute URL pointing at the web app, and every one of them is checked
/// against the configured origin list first. A returnUrl taken on trust is an
/// open redirect, and an open redirect on the sign-in path is the one that gets
/// used, because the link genuinely does come from the real site.
/// </summary>
public static class ReturnTargets
{
    public static string Denied(HttpContext context) => Resolve(context, null, "/auth/denied");

    public static string AfterSignIn(HttpContext context, string? requested) =>
        Resolve(context, requested, "/app/sessions");

    public static string AfterSignOut(HttpContext context) => Resolve(context, null, "/");

    private static string Resolve(HttpContext context, string? requested, string fallbackPath)
    {
        var origins = AllowedOrigins(context);
        var home = origins.FirstOrDefault() ?? "/";

        if (string.IsNullOrWhiteSpace(requested)) return Combine(home, fallbackPath);

        // Relative paths are the ordinary case and are safe by construction:
        // they cannot leave the web app. Everything else has to name an origin
        // we already trust.
        if (requested.StartsWith('/') && !requested.StartsWith("//")) return Combine(home, requested);

        if (Uri.TryCreate(requested, UriKind.Absolute, out var absolute))
        {
            var origin = $"{absolute.Scheme}://{absolute.Authority}";
            if (origins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return requested;
        }

        return Combine(home, fallbackPath);
    }

    private static string[] AllowedOrigins(HttpContext context) =>
        context.RequestServices.GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    private static string Combine(string origin, string path) => $"{origin.TrimEnd('/')}{path}";
}
