namespace LaunchService.Infrastructure.Auth;

/// <summary>
/// Dev/reference JWT validation settings (ADR-007). There is no Identity Provider here — these
/// values only let this Resource Server validate tokens minted by `scripts/dev-jwt/make_token.py`
/// for local tests. SigningKey must be overridden outside source control for anything beyond a
/// throwaway local demo.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The exact value documented in README.md/docker-compose.yml/appsettings.Development.json
    /// for local development — never a real secret (A-018). JwtAuthenticationExtensions refuses to
    /// start outside Development if the configured signing key equals this value.
    /// </summary>
    public const string DocumentedDevSigningKey = "cashflow-local-dev-signing-key-32-bytes-minimum-change-me";

    public string Issuer { get; set; } = "cashflow-dev-issuer";

    /// <summary>
    /// Shared by both services (ADR-007) — a single audience for the one API surface this
    /// deliverable exposes. Authorization still differs per endpoint through scopes.
    /// </summary>
    public string Audience { get; set; } = "cashflow-api";

    public string SigningKey { get; set; } = string.Empty;
}
