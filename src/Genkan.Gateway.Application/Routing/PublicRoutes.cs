namespace Genkan.Gateway.Application.Routing;

/// <summary>Routes the gateway lets through without a bearer token. Single source for the token check and the combined OpenAPI document.</summary>
public static class PublicRoutes
{
    private static readonly HashSet<string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/auth/login", "/auth/health", "/notifications/health",
    };

    public static bool IsPublic(string? path)
    {
        var trimmed = (path ?? "").TrimEnd('/');
        return trimmed.Length == 0 || Paths.Contains(trimmed);
    }
}
