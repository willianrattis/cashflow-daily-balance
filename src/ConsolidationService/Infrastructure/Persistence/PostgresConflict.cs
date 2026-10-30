namespace ConsolidationService.Infrastructure.Persistence;

/// <summary>
/// Reads the Postgres unique-violation (23505) constraint name out of an EF Core
/// <see cref="DbUpdateException"/>. Either the EventId primary key or the EntryId
/// unique index firing means "this event was already applied" — a safe no-op, not an error
/// (ADR-004).
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
