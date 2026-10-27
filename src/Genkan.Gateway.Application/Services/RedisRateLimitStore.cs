using Genkan.Shared.Configuration;
using Genkan.Gateway.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Genkan.Gateway.Application.Services;

/// <summary>
/// Token buckets kept in Redis, so every gateway replica draws from the same bucket. Same algorithm as
/// <see cref="MemoryRateLimitStore"/>, run as one Lua script: read, refill, take a token and write back are atomic, and the
/// time comes from the Redis server (TIME), so clock drift between replicas cannot skew the refill.
/// If Redis is unreachable the request is allowed (fail open) and a warning is logged once until Redis answers again.
/// </summary>
public sealed class RedisRateLimitStore(IConnectionMultiplexer redis, ILogger<RedisRateLimitStore> log) : IRateLimitStore
{
    private const string KeyPrefix = "genkan:ratelimit:";

    // KEYS[1] = bucket | ARGV[1] = capacity, ARGV[2] = tokens per second, ARGV[3] = key lifetime in ms
    // Returns { allowed (1/0), tokens left (floored), ms until one token is back }
    private const string Script = """
        local t = redis.call('TIME')
        local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        local capacity = tonumber(ARGV[1])
        local rate = tonumber(ARGV[2])

        local data = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(data[1])
        local ts = tonumber(data[2])
        if tokens == nil or ts == nil then
          tokens = capacity
          ts = now
        end

        tokens = math.min(capacity, tokens + math.max(0, now - ts) / 1000 * rate)

        local allowed = 0
        local wait = 0
        if tokens >= 1 then
          tokens = tokens - 1
          allowed = 1
        else
          wait = math.ceil((1 - tokens) / rate * 1000)
        end

        redis.call('HSET', KEYS[1], 'tokens', tostring(tokens), 'ts', tostring(now))
        redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[3]))
        return { allowed, math.floor(tokens), wait }
        """;

    private int _degraded;   // 1 while Redis is failing, so the warning is logged once instead of on every request

    public async ValueTask<RateLimitDecision> TryAcquireAsync(string key, RateLimitPolicyOptions policy, CancellationToken ct)
    {
        try
        {
            var perSecond = policy.TokensPerSecond;
            // A bucket idle for the time it takes to refill completely equals a new one, so Redis can drop it.
            var ttlMs = (long)Math.Ceiling((policy.Capacity / perSecond + 5) * 1000);

            var reply = await redis.GetDatabase()
                .ScriptEvaluateAsync(Script, [(RedisKey)(KeyPrefix + key)], [policy.Capacity, perSecond, ttlMs])
                .WaitAsync(ct);

            var values = ((RedisResult[])reply)!;
            var allowed = (int)values[0] == 1;
            var remaining = (int)values[1];
            var waitMs = (long)values[2];

            if (Volatile.Read(ref _degraded) == 1 && Interlocked.Exchange(ref _degraded, 0) == 1)
                log.LogInformation("Redis rate limiting is back");

            return new RateLimitDecision(allowed, remaining, allowed ? TimeSpan.Zero : TimeSpan.FromMilliseconds(waitMs));
        }
        catch (Exception ex) when ((ex is RedisException or TimeoutException) && !ct.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref _degraded, 1) == 0)
                log.LogWarning(ex, "Redis unavailable: rate limiting is skipped (requests allowed) until it answers again");

            return new RateLimitDecision(true, policy.Capacity, TimeSpan.Zero);
        }
    }
}
