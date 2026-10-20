namespace ConsolidationService.Infrastructure.Messaging;

public enum ConsumerFailureKind
{
    Transient,
    Unexpected
}

/// <summary>
/// Separates transient infrastructure failures (connection drop, timeout, Postgres briefly
/// unreachable or overloaded) from unexpected ones (a bug, a shape the projector cannot handle).
/// Transient failures must never reach the DLQ — a Consolidation DB outage, however long, must
/// not corrupt the projection or require manual DLQ replay (docs/05 §2, ADR-002).
/// </summary>
public static class ConsumerFailureClassifier
{
    public static ConsumerFailureKind Classify(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException postgres && IsTransientSqlState(postgres.SqlState))
            {
                return ConsumerFailureKind.Transient;
            }

            if (ex is NpgsqlException { IsTransient: true })
            {
                return ConsumerFailureKind.Transient;
            }

            if (ex is TimeoutException or SocketException or IOException)
            {
                return ConsumerFailureKind.Transient;
            }
        }

        return ConsumerFailureKind.Unexpected;
    }

    private static bool IsTransientSqlState(string? sqlState)
    {
        if (string.IsNullOrEmpty(sqlState))
        {
            return false;
        }

        // Class 08 (connection exception) and class 53 (insufficient resources): any code in
        // either class is safe to retry.
        if (sqlState.StartsWith("08", StringComparison.Ordinal) || sqlState.StartsWith("53", StringComparison.Ordinal))
        {
            return true;
        }

        // 57P01/57P02/57P03: admin/crash shutdown, cannot connect now. 57014: query canceled
        // (e.g. a statement/lock timeout on the server side) — the server is temporarily
        // unavailable to this query, not permanently rejecting it. 55P03: lock not available
        // (NOWAIT/lock_timeout) — another transaction is holding the row, retrying is the point.
        // 40001/40P01: serialization failure / deadlock detected — the transaction was rolled
        // back, retrying is the point.
        return sqlState is "57P01" or "57P02" or "57P03" or "57014" or "55P03" or "40001" or "40P01";
    }
}
