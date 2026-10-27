using Genkan.Gateway.Api.Configuration;
using Genkan.Gateway.Application.Interfaces;
using Genkan.Shared.Auth;
using Genkan.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Genkan.Gateway.Api.Middleware;

/// <summary>
/// Takes one token from the caller's bucket before the request goes on to the proxy. When the bucket is empty it answers
/// 429 with Retry-After (seconds until one token is back). Health endpoints are never limited.
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitStore _store;
    private readonly RateLimitingOptions _options;
    private readonly string _policyName;
    private readonly RateLimitPolicyOptions _policy;

    public RateLimitMiddleware(RequestDelegate next, IRateLimitStore store, IOptions<RateLimitingOptions> options, string policyName)
    {
        _next = next;
        _store = store;
        _options = options.Value;
        _policyName = policyName;
        _policy = _options.Policies.TryGetValue(policyName, out var p)
            ? p
            : throw new InvalidOperationException($"Rate limit policy '{policyName}' is not defined in the RateLimiting:Policies section.");
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (!_options.Enabled || path.EndsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var decision = await _store.TryAcquireAsync($"{_policyName}|{Partition(ctx)}", _policy, ctx.RequestAborted);

        ctx.Response.Headers["X-RateLimit-Limit"] = _policy.Capacity.ToString();
        ctx.Response.Headers["X-RateLimit-Remaining"] = decision.Remaining.ToString();

        if (!decision.Allowed)
        {
            var retryAfter = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.Response.Headers.RetryAfter = retryAfter.ToString();
            await ctx.Response.WriteAsJsonAsync(new { error = "rate_limited", retryAfterSeconds = retryAfter });
            return;
        }

        await _next(ctx);
    }

    private string Partition(HttpContext ctx)
    {
        // X-User-Id is only trustworthy after TokenValidationMiddleware (it strips the client's copy and sets its own).
        var userId = ctx.Request.Headers[GatewayHeaders.UserId].ToString();
        if (_policy.Key == RateLimitKey.User && userId.Length > 0) return $"user:{userId}";
        return $"ip:{ctx.Connection.RemoteIpAddress}";
    }
}
