using System.Security.Claims;
using Awana.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Awana.Api.Auth;

/// <summary>
/// Checks a signed-in session against the users table on every request.
/// </summary>
/// <remarks>
/// The cookie carries the role it was issued with and renews itself on use, so
/// without this a role change or a deactivation took effect only when the
/// person signed out, and somebody using the console every week never would.
/// Removing access has to actually remove it.
///
/// The cost is one primary key lookup per signed-in request. That is nothing
/// next to the queries those requests already make, and the public board sends
/// no cookie, so it never pays it.
/// </remarks>
public static class SessionRevalidation
{
    public enum Outcome
    {
        /// <summary>Nothing about the account has changed.</summary>
        Unchanged,

        /// <summary>Role or name changed; the session carries on with the new values.</summary>
        Refreshed,

        /// <summary>Gone or deactivated; the session ends.</summary>
        Revoked,
    }

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null) return;

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AwanaDbContext>();

        (Outcome Outcome, ClaimsPrincipal? Principal) result;

        try
        {
            result = await CheckAsync(
                db, context.Principal, context.Scheme.Name, context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A database blip keeps the session as it was rather than signing
            // the scorekeeper out mid-round. Anything that needs the database
            // for the request itself will fail on its own, loudly; ending the
            // session as well would only add a trip back through Google to a
            // bad moment.
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(SessionRevalidation))
                .LogWarning(ex, "Could not re-check session for user {UserId}; keeping it", context.Principal.Id());

            return;
        }

        switch (result.Outcome)
        {
            case Outcome.Revoked:
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                break;

            case Outcome.Refreshed:
                context.ReplacePrincipal(result.Principal!);
                // Rewrites the cookie, so the next request starts from the
                // current role instead of being corrected all over again.
                context.ShouldRenew = true;
                break;
        }
    }

    /// <summary>
    /// The decision, apart from the cookie machinery, so it can be tested
    /// directly against a database.
    /// </summary>
    public static async Task<(Outcome Outcome, ClaimsPrincipal? Principal)> CheckAsync(
        AwanaDbContext db, ClaimsPrincipal principal, string authenticationType, CancellationToken ct = default)
    {
        // A cookie this app issued always has an id. One without is not a
        // session worth keeping.
        if (principal.Id() is not { } id) return (Outcome.Revoked, null);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null || !user.IsActive) return (Outcome.Revoked, null);

        var unchanged =
            principal.RoleOf() == user.Role &&
            principal.FindFirstValue(ClaimTypes.Name) == user.DisplayName;

        return unchanged
            ? (Outcome.Unchanged, principal)
            : (Outcome.Refreshed, AuthClaims.ToPrincipal(user, authenticationType));
    }
}
