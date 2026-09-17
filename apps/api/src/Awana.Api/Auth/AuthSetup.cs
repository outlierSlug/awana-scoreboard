using System.Security.Claims;
using Awana.Data;
using Awana.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Auth;

public static class AuthPolicies
{
    public const string Scorekeeper = "scorekeeper";
    public const string GamesLeader = "games-leader";
    public const string Admin = "admin";
}

public static class AuthSetup
{
    /// <summary>
    /// Cookie session, with Google as the only way to establish one.
    /// </summary>
    /// <remarks>
    /// Google is registered only when it is configured. Without that check the
    /// handler throws at startup for want of a client id, which would mean the
    /// API could not run at all on a machine that has not been given OAuth
    /// credentials yet. Sign-in then fails with an explanation instead, and
    /// everything that does not need a signed-in user still works.
    /// </remarks>
    public static IServiceCollection AddAwanaAuth(
        this IServiceCollection services, IConfiguration configuration)
    {
        var google = configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>()
            ?? new GoogleAuthOptions();

        var auth = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;

            // The cookie answers an unauthenticated request, not Google.
            //
            // Making Google the default challenge sends a 302 to
            // accounts.google.com in reply to an ordinary API call: the browser
            // follows it, CORS refuses it, and a fetch that should have been a
            // plain 401 surfaces as a network error nobody can act on. Sign-in
            // names the Google scheme explicitly, which is the only place a
            // redirect to a provider belongs.
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });

        auth.AddCookie(options =>
        {
            options.Cookie.Name = "awana.session";
            options.Cookie.HttpOnly = true;

            // Lax rather than None. The web app and the API are same-site in
            // every environment by design: subdomains of one registrable domain
            // in production, and localhost on two ports locally, where the port
            // is not part of the site.
            options.Cookie.SameSite = SameSiteMode.Lax;

            // Always, not SameAsRequest.
            //
            // SameAsRequest decides from the scheme the app believes it is
            // serving, and behind a TLS terminating proxy that is http unless
            // the forwarded headers are being read. So the one situation where
            // the flag matters most is exactly the situation where
            // SameAsRequest quietly drops it, and the result is a session
            // cookie travelling unprotected with nothing to show for it.
            //
            // This costs nothing locally: browsers treat http://localhost as a
            // secure context, so a Secure cookie is still set and still sent.
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            // This is an API. A browser fetch cannot follow a redirect to a
            // sign-in page and would report the HTML as a parse failure, so the
            // status code is the answer and the web app decides what to show.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };

            // Role changes and deactivations from the People page apply on the
            // next request rather than whenever the person next signs in.
            options.Events.OnValidatePrincipal = SessionRevalidation.ValidateAsync;
        });

        if (google.IsConfigured)
        {
            auth.AddGoogle(options =>
            {
                options.ClientId = google.ClientId;
                options.ClientSecret = google.ClientSecret;

                // Registered in Google Cloud Console against the API's origin,
                // since this is where Google sends the code back.
                options.CallbackPath = "/signin-google";

                // Nothing here calls a Google API on the user's behalf, so
                // keeping their tokens would be storing a credential for no
                // reason.
                options.SaveTokens = false;

                options.Events.OnTicketReceived = OnTicketReceivedAsync;
            });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.Scorekeeper, policy =>
                policy.RequireAssertion(c => c.User.AtLeast(UserRole.Scorekeeper)));
            options.AddPolicy(AuthPolicies.GamesLeader, policy =>
                policy.RequireAssertion(c => c.User.AtLeast(UserRole.GamesLeader)));
            options.AddPolicy(AuthPolicies.Admin, policy =>
                policy.RequireAssertion(c => c.User.AtLeast(UserRole.Admin)));
        });

        return services;
    }

    /// <summary>
    /// Google has said who this is. Decide whether they are allowed in.
    /// </summary>
    /// <remarks>
    /// A Google account is proof of identity and nothing else. Membership is
    /// the allowlist seeded into the users table, and an unknown address is
    /// turned away rather than quietly given a Viewer account: this is a
    /// scoreboard for one club's volunteers, and anyone with a Google account
    /// can reach the sign-in button.
    /// </remarks>
    private static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var email = context.Principal?.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        var db = context.HttpContext.RequestServices.GetRequiredService<AwanaDbContext>();

        var user = email is null
            ? null
            : await db.Users.FirstOrDefaultAsync(u => u.Email == email, context.HttpContext.RequestAborted);

        if (user is null || !user.IsActive)
        {
            context.HandleResponse();
            context.Response.Redirect(ReturnTargets.Denied(context.HttpContext));
            return;
        }

        // Google's subject is stable across an email change, so it becomes the
        // identity once we have seen it. The seeded row only had an address.
        user.GoogleSubject ??= subject;
        user.LastLoginAt = DateTimeOffset.UtcNow;

        // The seeder can only put the address in this column, because an
        // address is all an allowlist has. This is the moment the real name
        // becomes available, and taking it every time rather than once means a
        // volunteer who changes their name in their Google account is not
        // stuck with the old one here.
        var googleName = context.Principal?.FindFirstValue(ClaimTypes.Name)?.Trim();
        if (!string.IsNullOrWhiteSpace(googleName)) user.DisplayName = googleName;

        await db.SaveChangesAsync(context.HttpContext.RequestAborted);

        // Replace Google's ticket with our own claims, so nothing downstream
        // depends on the shape of an external provider's token.
        context.Principal = AuthClaims.ToPrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme);
        context.Properties!.IsPersistent = true;
    }
}

/// <summary>Google OAuth credentials, absent until somebody creates them.</summary>
public class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
