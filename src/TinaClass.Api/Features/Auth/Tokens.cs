using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Contracts.Auth;

namespace TinaClass.Api.Features.Auth;

/// <summary><c>Jwt:*</c>. <see cref="SigningKey"/> is required outside Development: at least 32 characters, kept secret.</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "tinaclass-api";

    public string Audience { get; set; } = "tinaclass-app";

    public string? SigningKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;
}

/// <summary>
/// The key access tokens are signed with: from configuration (an environment variable, a secret store), or, in Development
/// only, made up at start. Any other environment without a key of 32 or more characters refuses to start.
/// </summary>
public sealed class JwtKeyProvider
{
    public const int MinimumLength = 32;

    public JwtKeyProvider(IOptions<JwtOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.SigningKey;
        if (!string.IsNullOrEmpty(configured))
        {
            if (configured.Length < MinimumLength)
            {
                throw new InvalidOperationException($"Jwt:SigningKey must be at least {MinimumLength} characters.");
            }
            Key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configured));
        }
        else if (environment.IsDevelopment())
        {
            Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
            Generated = true;
        }
        else
        {
            throw new InvalidOperationException(
                $"Set Jwt:SigningKey (the environment variable Jwt__SigningKey, or a secret store): {MinimumLength} or more random characters. " +
                "Only the Development environment makes one up.");
        }
    }

    public SymmetricSecurityKey Key { get; }

    /// <summary>True when the key was made up at start: tokens stop working when the API restarts.</summary>
    public bool Generated { get; }
}

/// <summary>Issues the access token (JWT) and refresh token of a session.</summary>
public sealed class TokenIssuer(JwtKeyProvider keys, IOptions<JwtOptions> options, TimeProvider time)
{
    public const string RefreshPrefix = "rt_";

    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>A new pair for <paramref name="session"/>; its refresh token hash is set, the caller saves it.</summary>
    public TokenPair Issue(User user, Session session)
    {
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(options.Value.AccessTokenMinutes);
        var access = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [AppClaims.Subject] = user.Id.ToString(),
                [AppClaims.Name] = user.Name,
                [AppClaims.Session] = session.Id.ToString(),
            },
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.HmacSha256),
        });
        var refresh = RefreshPrefix + Secrets.Token(32);
        session.PreviousTokenHash = session.TokenHash;
        session.TokenHash = Secrets.Hash(refresh);
        return new TokenPair(access, expires, refresh);
    }
}
