using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Api.Errors;

namespace NovaWallet.Api.Endpoints;

public static class DevTokenEndpoint
{
    public record DevTokenRequest(string Sub, string Role);
    public record DevTokenResponse(string Token, DateTimeOffset ExpiresAt);

    public static RouteGroupBuilder MapDevToken(this WebApplication app)
    {
        var group = app.MapGroup("/dev").WithTags("Dev");

        group.MapPost("/token", (
            DevTokenRequest body,
            IConfiguration config,
            IHostEnvironment env) =>
        {
            if (!env.IsDevelopment() && !config.GetValue("DevAuth:Enabled", false))
                return Results.NotFound();

            if (string.IsNullOrWhiteSpace(body.Sub))
                throw new AppException(400, "invalid_request", "sub is required");

            var jwt = config.GetSection("Jwt");
            var key = jwt["Key"]!;
            var issuer = jwt["Issuer"]!;
            var audience = jwt["Audience"]!;
            var hours = config.GetValue("Jwt:TokenHours", 24);

            var expires = DateTimeOffset.UtcNow.AddHours(hours);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, body.Sub.Trim()),
                new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(body.Role) ? "customer" : body.Role.Trim()),
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: expires.UtcDateTime,
                signingCredentials: credentials);

            var jwtString = new JwtSecurityTokenHandler().WriteToken(token);
            return Results.Ok(new DevTokenResponse(jwtString, expires));
        })
        .AllowAnonymous();

        return group;
    }
}
