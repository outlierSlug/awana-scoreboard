using System.Security.Claims;
using Awana.Data.Entities;

namespace Awana.Api.Auth;

/// <summary>
/// What the session cookie carries, and how to read it back.
///
/// Everything an endpoint needs to authorize a request is in the cookie, so the
/// common path costs no database round trip. The trade is that a role change
/// does not take effect until the person signs in again, which for roughly ten
/// volunteers is the right side of that trade.
/// </summary>
public static class AuthClaims
{
    public const string UserId = "awana:uid";
    public const string ChurchId = "awana:church";

    /// <summary>
    /// The role as its numeric value, not its name.
    ///
    /// UserRole is deliberately ordered so a policy can ask for "at least
    /// this", and comparing numbers is the only way to keep that property. A
    /// name would force every policy to list the roles above it, which is the
    /// list that gets forgotten when a role is added.
    /// </summary>
    public const string Role = "awana:role";

    public static ClaimsPrincipal ToPrincipal(AppUser user, string scheme)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(UserId, user.Id.ToString()),
                new Claim(ChurchId, user.ChurchId.ToString()),
                new Claim(Role, ((int)user.Role).ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.DisplayName),
            ],
            scheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    public static Guid? Id(this ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(UserId), out var id) ? id : null;

    public static Guid? Church(this ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(ChurchId), out var id) ? id : null;

    public static UserRole? RoleOf(this ClaimsPrincipal? principal) =>
        int.TryParse(principal?.FindFirstValue(Role), out var role) ? (UserRole)role : null;

    public static bool AtLeast(this ClaimsPrincipal? principal, UserRole minimum) =>
        principal.RoleOf() is { } role && role >= minimum;
}
