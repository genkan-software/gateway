using Genkan.Shared.Configuration;
using Genkan.Gateway.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Genkan.Gateway.Application.Services;

/// <summary>
/// Buckets kept in this process. No timer is needed: the refill is computed from the time elapsed since the last request.
/// A bucket idle for the time it takes to refill completely is identical to a new one, so the cache simply drops it.
/// With several gateway instances every instance has its own buckets (use the Redis store to share them).
/// </summary>
public sealed class MemoryRateLimitStore(IMemoryCache cache, TimeProvider time) : IRateLimitStore
{
    private sealed class Bucket(double tokens, long timestamp)
    {
        public readonly object Gate = new();
        public double Tokens = tokens;
        public long Timestamp = timestamp;
    }

    public ValueTask<RateLimitDecision> TryAcquireAsync(string key, RateLimitPolicyOptions policy, CancellationToken ct)
    {
        var now = time.GetTimestamp();
        var perSecond = policy.TokensPerSecond;

        var bucket = cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromSeconds(policy.Capacity / perSecond + 5);
            return new Bucket(policy.Capacity, now);
        })!;

        lock (bucket.Gate)
        {
            var elapsed = time.GetElapsedTime(bucket.Timestamp, now).TotalSeconds;
            bucket.Tokens = Math.Min(policy.Capacity, bucket.Tokens + Math.Max(0, elapsed) * perSecond);
            bucket.Timestamp = now;

            if (bucket.Tokens >= 1)
            {
                bucket.Tokens -= 1;
                return ValueTask.FromResult(new RateLimitDecision(true, (int)bucket.Tokens, TimeSpan.Zero));
            }

            var wait = TimeSpan.FromSeconds((1 - bucket.Tokens) / perSecond);
            return ValueTask.FromResult(new RateLimitDecision(false, 0, wait));
        }
    }
}
