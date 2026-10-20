namespace LaunchService.Infrastructure.Auth;

/// <summary>Policy names and scope mapping from docs/03-contratos.md.</summary>
public static class AuthorizationPolicies
{
    public const string LaunchesWrite = "LaunchesWrite";
    public const string LaunchesRead = "LaunchesRead";

    public static void AddCashFlowPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(LaunchesWrite, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasScope(ctx.User, "cashflow.launches.write")));

        options.AddPolicy(LaunchesRead, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasScope(ctx.User, "cashflow.launches.read")));
    }

    private static bool HasScope(ClaimsPrincipal user, string scope)
        => user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
