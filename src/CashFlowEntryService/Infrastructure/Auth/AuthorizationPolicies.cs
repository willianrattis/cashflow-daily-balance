namespace CashFlowEntryService.Infrastructure.Auth;

/// <summary>Policy names and scope mapping from docs/03-contratos.md.</summary>
public static class AuthorizationPolicies
{
    public const string EntriesWrite = "EntriesWrite";
    public const string EntriesRead = "EntriesRead";

    public static void AddCashFlowPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(EntriesWrite, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasScope(ctx.User, "cashflow.entries.write")));

        options.AddPolicy(EntriesRead, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            HasScope(ctx.User, "cashflow.entries.read")));
    }

    private static bool HasScope(ClaimsPrincipal user, string scope)
        => user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
