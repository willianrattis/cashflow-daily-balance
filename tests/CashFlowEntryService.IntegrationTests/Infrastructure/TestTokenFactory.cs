namespace CashFlowEntryService.IntegrationTests.Infrastructure;

/// <summary>Mints HS256 tokens matching <see cref="CashFlowEntryServiceTestFixture"/>'s Jwt:* overrides.</summary>
public static class TestTokenFactory
{
    public static string Create(
        string? scope = null,
        string subject = "test-user",
        string? issuer = null,
        string? audience = null,
        string? signingKey = null,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var claims = new List<Claim>
        {
            new("sub", subject)
        };

        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? CashFlowEntryServiceTestFixture.TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer ?? CashFlowEntryServiceTestFixture.TestIssuer,
            audience: audience ?? CashFlowEntryServiceTestFixture.TestAudience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
