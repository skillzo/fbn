using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using NovaWallet.Api.Errors;

namespace NovaWallet.Api.Auth;

public static class ClaimsExtensions
{
    public static string GetCustomerId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(sub))
            throw new AppException(401, "unauthorized", "Token has no subject");
        return sub;
    }
}
