namespace LaunchService.Infrastructure.Auth;

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Reads the Jwt section once at build time, refuses to start with a missing or (outside
    /// Development) documented signing key, then registers JWT bearer authentication and the
    /// scope-based authorization policies.
    /// </summary>
    public static WebApplicationBuilder AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey não configurada. Veja README.md para gerar um valor de desenvolvimento.");
        }
        // Fora de Development, nem chave vazia nem a chave de desenvolvimento documentada (A-018) são
        // aceitáveis — a primeira já falharia acima por outro motivo (nenhuma seção Jwt configurada
        // fora de appsettings.Development.json), mas a segunda só é pega aqui.
        if (!builder.Environment.IsDevelopment() && jwtOptions.SigningKey == JwtOptions.DocumentedDevSigningKey)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey está usando o valor de desenvolvimento documentado fora do ambiente " +
                "Development. Configure uma chave de produção antes de implantar.");
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    RoleClaimType = "role",
                    NameClaimType = "sub"
                };
            });

        builder.Services.AddAuthorization(options => options.AddCashFlowPolicies());

        return builder;
    }
}
