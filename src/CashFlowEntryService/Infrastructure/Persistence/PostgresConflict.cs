namespace CashFlowEntryService.Infrastructure.Persistence;

/// <summary>
/// Reads the Postgres unique-violation (23505) constraint name out of an EF Core
/// <see cref="DbUpdateException"/>. This is the authoritative way concurrent inserts are
/// resolved — the database constraint decides, not an in-memory pre-check (ADR-004).
/// </summary>
public static class PostgresConflict
{
    public const string UniqueViolationSqlState = "23505";

    public static bool TryGetUniqueViolationConstraint(this DbUpdateException exception, out string constraintName)
    {
        if (exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState } pgException
            && pgException.ConstraintName is not null)
        {
            constraintName = pgException.ConstraintName;
            return true;
        }

        constraintName = string.Empty;
        return false;
    }
}
