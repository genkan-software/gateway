using Genkan.Gateway.Application.Interfaces;
using Genkan.Gateway.Application.Models;
using Genkan.Gateway.Application.Routing;
using Genkan.Shared.Auth;

namespace Genkan.Gateway.Api.Middleware;

/// <summary>
/// HTTP side of the token check: reads the bearer token, asks <see cref="ISessionValidator"/>, then forwards the identity
/// to the services as X-User-* headers. Client-supplied X-User-* headers are always stripped first.
/// </summary>
public sealed class TokenValidationMiddleware(RequestDelegate next, ISessionValidator sessions)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Request.Headers.Remove(GatewayHeaders.UserId);
        ctx.Request.Headers.Remove(GatewayHeaders.UserRole);
        ctx.Request.Headers.Remove(GatewayHeaders.UserEmail);

        if (PublicRoutes.IsPublic(ctx.Request.Path.Value))
        {
            await next(ctx);
            return;
        }

        var authorization = ctx.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await Reject(ctx, StatusCodes.Status401Unauthorized, "missing_token");
            return;
        }

        var result = await sessions.ValidateAsync(
            authorization,
            ctx.Request.Headers[GatewayHeaders.CorrelationId].ToString(),
            ctx.RequestAborted);

        switch (result)
        {
            case { Status: SessionValidationStatus.Valid, Session: { } session }:
                ctx.Request.Headers[GatewayHeaders.UserId] = session.UserId.ToString();
                ctx.Request.Headers[GatewayHeaders.UserRole] = session.Role;
                ctx.Request.Headers[GatewayHeaders.UserEmail] = session.Email;
                await next(ctx);
                break;

            case { Status: SessionValidationStatus.AuthUnavailable }:
                await Reject(ctx, StatusCodes.Status503ServiceUnavailable, "auth_unavailable");
                break;

            default:
                await Reject(ctx, StatusCodes.Status401Unauthorized, "invalid_or_expired_token");
                break;
        }
    }

    private static Task Reject(HttpContext ctx, int status, string error)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error });
    }
}
