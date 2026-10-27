using Genkan.Gateway.Api.Middleware;

namespace Genkan.Gateway.Api.Extensions;

public static class MiddlewareExtensions
{
    /// <summary>Checks the bearer token on every non-public route and forwards the identity as X-User-* headers.</summary>
    public static IApplicationBuilder UseTokenValidation(this IApplicationBuilder app) =>
        app.UseMiddleware<TokenValidationMiddleware>();

    /// <summary>Applies the named policy. Place it after UseTokenValidation to limit per user, or before it to limit per IP.</summary>
    public static IApplicationBuilder UseGatewayRateLimit(this IApplicationBuilder app, string policy) =>
        app.UseMiddleware<RateLimitMiddleware>(policy);
}
