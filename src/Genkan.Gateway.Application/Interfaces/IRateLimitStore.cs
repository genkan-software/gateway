using Genkan.Shared.Configuration;

namespace Genkan.Gateway.Application.Interfaces;

public readonly record struct RateLimitDecision(bool Allowed, int Remaining, TimeSpan RetryAfter);

/// <summary>Holds the token buckets. Same algorithm everywhere; only where the buckets live differs.</summary>
public interface IRateLimitStore
{
    /// <summary>Refills the bucket for the time elapsed, then tries to take one token.</summary>
    ValueTask<RateLimitDecision> TryAcquireAsync(string key, RateLimitPolicyOptions policy, CancellationToken ct);
}
