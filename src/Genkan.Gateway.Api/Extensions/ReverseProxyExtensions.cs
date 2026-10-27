namespace Genkan.Gateway.Api.Extensions;

public static class ReverseProxyExtensions
{
    /// <summary>Loads the YARP routes and clusters from the "ReverseProxy" section.</summary>
    public static IServiceCollection AddGatewayProxy(this IServiceCollection services, IConfiguration config)
    {
        services.AddReverseProxy().LoadFromConfig(config.GetSection("ReverseProxy"));
        return services;
    }
}
