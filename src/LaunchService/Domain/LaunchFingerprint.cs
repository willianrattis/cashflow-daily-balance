namespace LaunchService.Domain;

/// <summary>
/// Canonical hash of the business-relevant fields of a launch command. Used to detect whether a
/// retried Idempotency-Key carries the same payload or a conflicting one (ADR-004).
/// </summary>
public static class LaunchFingerprint
{
    public static string Compute(long amountInCents, LaunchNature nature, string? description)
    {
        var canonical = string.Join(
            '|',
            amountInCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            nature.ToString(),
            description ?? string.Empty);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
