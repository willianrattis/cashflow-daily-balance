namespace LaunchService.OutboxPublisherTests.Infrastructure;

/// <summary>Mints HS256 tokens matching <see cref="OutboxPublisherTestFixture"/>'s Jwt:* overrides.</summary>
public static class TestTokenFactory
{
    public static string Create(string? scope = null, string subject = "test-user")
    {
        var claims = new List<Claim>
        {
            new("sub", subject)
        };

        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(OutboxPublisherTestFixture.TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: OutboxPublisherTestFixture.TestIssuer,
            audience: OutboxPublisherTestFixture.TestAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
