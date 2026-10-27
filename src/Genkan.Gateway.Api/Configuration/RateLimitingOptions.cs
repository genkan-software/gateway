using Genkan.Shared.Configuration;

namespace Genkan.Gateway.Api.Configuration;

public enum RateLimitStoreKind
{
    /// <summary>Buckets live in each gateway process (every replica has its own).</summary>
    Memory,
    /// <summary>Buckets live in Redis (ConnectionStrings:Redis) and are shared by every replica.</summary>
    Redis,
}

/// <summary>Section "RateLimiting" of the gateway. Each policy is a <see cref="RateLimitPolicyOptions"/> (in Shared because the bucket stores use it too).</summary>
public sealed class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;
    public RateLimitStoreKind Store { get; set; } = RateLimitStoreKind.Memory;
    public Dictionary<string, RateLimitPolicyOptions> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsValid() => Policies.Values.All(p => p.IsValid());
}
