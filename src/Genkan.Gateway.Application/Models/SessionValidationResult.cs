namespace Genkan.Gateway.Application.Models;

public enum SessionValidationStatus
{
    /// <summary>The auth service accepted the token.</summary>
    Valid,
    /// <summary>The token is unknown, expired or malformed.</summary>
    Invalid,
    /// <summary>The auth service could not be reached, so the token could not be checked.</summary>
    AuthUnavailable,
}

public sealed record SessionValidationResult(SessionValidationStatus Status, SessionDto? Session = null)
{
    public static readonly SessionValidationResult Rejected = new(SessionValidationStatus.Invalid);
    public static readonly SessionValidationResult Unavailable = new(SessionValidationStatus.AuthUnavailable);

    public static SessionValidationResult Success(SessionDto session) => new(SessionValidationStatus.Valid, session);
}
