using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Presentation.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// JWT bearer authentication, and AU-6's central rule: a fallback authorization policy that
    /// requires a signed-in user on every endpoint unless it is marked <see cref="AllowAnonymousAttribute"/>.
    /// New endpoints are therefore protected by default; nothing has to remember an [Authorize].
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptions.Validator>();

        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var settings = jwt.Value;

                // Keep claim names as issued (sub, email, name) instead of mapping to long URIs.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = settings.CreateSigningKey(),
                    // Only the algorithm we sign with; rejects "none" and algorithm swaps.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds),
                    NameClaimType = JwtRegisteredClaimNames.Name
                };

                bearer.Events = new JwtBearerEvents { OnChallenge = WriteUnauthorizedProblemAsync };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>
    /// A 401 in the API's usual ProblemDetails shape instead of an empty body, saying whether the
    /// session expired so the client can explain it.
    /// </summary>
    private static Task WriteUnauthorizedProblemAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
            ? "Bearer"
            : "Bearer error=\"invalid_token\"";

        var problem = new ProblemDetails
        {
            Title = "Unauthorized",
            Status = StatusCodes.Status401Unauthorized,
            Detail = expired ? "Your session has expired. Sign in again." : "Sign in to use the API."
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return context.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null, contentType: "application/problem+json");
    }
}
