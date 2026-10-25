using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ExpenseGuard.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ExpenseGuard.Api;

public sealed record Identity(string Token, string Employee, Role Role);
public sealed class KeyAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, Identity[] identities)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-ExpenseGuard-Key", out var supplied) || supplied.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()));
        Identity? matched = null;
        foreach (var identity in identities)
            if (CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.UTF8.GetBytes(identity.Token)))) matched = identity;
        if (matched is null) return Task.FromResult(AuthenticateResult.Fail("Invalid key."));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, matched.Employee), new Claim(ClaimTypes.Role, matched.Role.ToString())], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
