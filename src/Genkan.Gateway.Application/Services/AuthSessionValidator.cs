using System.Net.Http.Json;
using Genkan.Gateway.Application.Interfaces;
using Genkan.Gateway.Application.Models;
using Genkan.Shared.Auth;
using Microsoft.Extensions.Logging;

namespace Genkan.Gateway.Application.Services;

/// <summary>Validates the bearer token with GET /auth/session on the auth service.</summary>
public sealed class AuthSessionValidator(IHttpClientFactory httpFactory, ILogger<AuthSessionValidator> log) : ISessionValidator
{
    public const string HttpClientName = "auth";

    public async Task<SessionValidationResult> ValidateAsync(string authorization, string correlationId, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/auth/session");
            req.Headers.TryAddWithoutValidation("Authorization", authorization);
            req.Headers.TryAddWithoutValidation(GatewayHeaders.CorrelationId, correlationId);

            using var res = await httpFactory.CreateClient(HttpClientName).SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return SessionValidationResult.Rejected;

            var session = await res.Content.ReadFromJsonAsync<SessionDto>(ct);
            return session is null ? SessionValidationResult.Rejected : SessionValidationResult.Success(session);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Auth service unreachable");
            return SessionValidationResult.Unavailable;
        }
    }
}
