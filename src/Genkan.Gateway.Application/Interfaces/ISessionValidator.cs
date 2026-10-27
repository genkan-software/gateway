using Genkan.Gateway.Application.Models;

namespace Genkan.Gateway.Application.Interfaces;

/// <summary>Checks a bearer token against the auth service. No HTTP-pipeline concern lives here: the caller decides what to answer.</summary>
public interface ISessionValidator
{
    /// <param name="authorization">Raw Authorization header value ("Bearer ...").</param>
    /// <param name="correlationId">Forwarded to the auth service so both sides log the same id.</param>
    Task<SessionValidationResult> ValidateAsync(string authorization, string correlationId, CancellationToken ct);
}
