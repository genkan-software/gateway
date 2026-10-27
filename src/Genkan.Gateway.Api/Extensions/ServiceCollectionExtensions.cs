using Genkan.Gateway.Api.Configuration;
using Genkan.Gateway.Application.Interfaces;
using Genkan.Gateway.Application.Services;
using Genkan.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Genkan.Gateway.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers everything the gateway's business logic needs: token validation, rate limiting and the combined Swagger document.</summary>
    public static IServiceCollection AddGatewayApplication(this IServiceCollection services, IConfiguration config) =>
        services
            .AddGatewaySessionValidation(config)
            .AddGatewayRateLimiting(config)
            .AddGatewaySwagger(config);

    /// <summary>Registers the auth client (Services:AuthUrl, 5 s timeout) and the validator that uses it.</summary>
    public static IServiceCollection AddGatewaySessionValidation(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient(AuthSessionValidator.HttpClientName, c =>
        {
            c.BaseAddress = new Uri(config["Services:AuthUrl"]!);
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<ISessionValidator, AuthSessionValidator>();
        return services;
    }

    /// <summary>Binds the "RateLimiting" section (validated at startup) and registers the bucket store chosen by RateLimiting:Store (Memory | Redis).</summary>
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(config.GetSection("RateLimiting"))
            .Validate(o => o.IsValid(), "RateLimiting: every policy needs Capacity >= 1, RefillTokens > 0 and RefillPeriodSeconds > 0.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        if (config.GetValue<RateLimitStoreKind>("RateLimiting:Store") == RateLimitStoreKind.Redis)
        {
            var redis = config.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redis))
                throw new InvalidOperationException("RateLimiting:Store is Redis but ConnectionStrings:Redis is not set.");

            // abortConnect=false in the connection string lets the gateway start (and keep serving) while Redis is down.
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
            services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
        }
        else
        {
            services.AddMemoryCache();
            services.AddSingleton<IRateLimitStore, MemoryRateLimitStore>();
        }

        return services;
    }

    /// <summary>Binds the "Swagger" section and registers the aggregator. Nothing is exposed until the Api project maps /swagger with Enabled=true.</summary>
    public static IServiceCollection AddGatewaySwagger(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SwaggerOptions>(config.GetSection(SwaggerOptions.Section));
        services.AddHttpClient(OpenApiAggregator.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<OpenApiAggregator>();
        return services;
    }
}
