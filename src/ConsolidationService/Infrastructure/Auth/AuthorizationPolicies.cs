namespace ConsolidationService.Infrastructure.Auth;

/// <summary>Policy names and scope mapping from docs/03-contratos.md.</summary>
public static class AuthorizationPolicies
{
    public const string ConsolidationRead = "ConsolidationRead";

    public static void AddCashFlowPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(ConsolidationRead, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasScope(ctx.User, "cashflow.consolidation.read")));
    }

    private static bool HasScope(ClaimsPrincipal user, string scope)
        => user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
