namespace Genkan.Gateway.Application.Models;

/// <summary>Identity returned by the auth service (GET /auth/session) for a valid bearer token.</summary>
public sealed record SessionDto(Guid UserId, string Email, string Role, string? DisplayName);
