using System.Text.Json;
using System.Text.Json.Nodes;
using Genkan.Shared.Configuration;
using Genkan.Gateway.Application.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Genkan.Gateway.Application.Services;

/// <summary>
/// Downloads the OpenAPI document of every configured service and merges them into one document served by the gateway.
/// Paths are already gateway paths (the services route under /auth and /notifications), so they are merged as they are.
/// Server entries are dropped so "Try it out" calls the gateway itself, with a bearer token.
/// </summary>
public sealed class OpenApiAggregator(IHttpClientFactory httpFactory, IOptions<SwaggerOptions> options, ILogger<OpenApiAggregator> log)
{
    public const string HttpClientName = "openapi";

    private const string SchemaRefPrefix = "#/components/schemas/";
    private const string GatewayHealthPath = "/health";   // answered by the gateway itself, never by a service

    private static readonly HashSet<string> HttpMethodKeys = new(StringComparer.Ordinal)
    {
        "get", "put", "post", "delete", "options", "head", "patch", "trace",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;
    private DateTimeOffset _expiresAt;

    /// <summary>The merged document as JSON text (cached for <see cref="SwaggerOptions.CacheSeconds"/>).</summary>
    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt) return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt) return _cached;

            var (json, failures) = await BuildAsync(ct);
            var ttl = options.Value.CacheSeconds;
            if (failures > 0) ttl = Math.Min(ttl, 5);   // retry soon when a service was down

            _cached = json;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(ttl, 0));
            return json;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Json, int Failures)> BuildAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpFactory.CreateClient(HttpClientName);

        var results = await Task.WhenAll(opts.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .Select(s => FetchAsync(client, s, ct)));

        var available = results.Where(r => r.Doc is not null).ToList();
        var unavailable = results.Where(r => r.Doc is null).Select(r => r.Name).ToList();

        var description = $"Combined OpenAPI document of: {string.Join(", ", available.Select(r => r.Name))}.";
        if (unavailable.Count > 0) description += $" Currently unreachable: {string.Join(", ", unavailable)}.";

        var paths = new JsonObject { [GatewayHealthPath] = GatewayHealthItem() };
        var components = new JsonObject
        {
            ["securitySchemes"] = new JsonObject
            {
                ["Bearer"] = new JsonObject
                {
                    ["type"] = "http",
                    ["scheme"] = "bearer",
                    ["description"] = "Session token returned by POST /auth/login.",
                },
            },
        };
        var tags = new JsonArray();
        var seenTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, doc) in available.Select(r => (r.Name, Doc: r.Doc!)))
        {
            RenameSchemas(doc, FindSchemaCollisions(name, doc, components["schemas"] as JsonObject));
            MergePaths(name, doc, paths);
            MergeComponents(doc, components);
            MergeTags(doc, tags, seenTags);
        }

        var merged = new JsonObject
        {
            ["openapi"] = available.Select(r => ReadString(r.Doc!["openapi"])).FirstOrDefault(v => v is not null) ?? "3.0.1",
            ["info"] = new JsonObject { ["title"] = opts.Title, ["version"] = "v1", ["description"] = description },
            // Every operation needs the bearer token by default; the public ones override this with an empty list.
            ["security"] = new JsonArray(new JsonObject { ["Bearer"] = new JsonArray() }),
            ["tags"] = tags,
            ["paths"] = paths,
            ["components"] = components,
        };

        return (merged.ToJsonString(), unavailable.Count);
    }

    private async Task<(string Name, JsonObject? Doc)> FetchAsync(HttpClient client, SwaggerSource source, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(source.Name) ? source.Url : source.Name;
        try
        {
            var text = await client.GetStringAsync(source.Url, ct);
            if (JsonNode.Parse(text) is JsonObject doc) return (name, doc);
            log.LogWarning("OpenAPI source {Source} ({Url}) did not return a JSON object", name, source.Url);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException) && !ct.IsCancellationRequested)
        {
            log.LogWarning("OpenAPI source {Source} ({Url}) unavailable: {Reason}", name, source.Url, ex.Message);
        }

        return (name, null);
    }

    private void MergePaths(string source, JsonObject doc, JsonObject paths)
    {
        if (doc["paths"] is not JsonObject docPaths) return;

        foreach (var (path, item) in docPaths)
        {
            if (item is not JsonObject itemObj || path.Equals(GatewayHealthPath, StringComparison.OrdinalIgnoreCase)) continue;

            if (paths[path] is not JsonObject target)
            {
                target = new JsonObject();
                paths[path] = target;
            }

            foreach (var (key, value) in itemObj)
            {
                if (value is null) continue;
                if (target.ContainsKey(key))
                {
                    log.LogWarning("{Key} {Path} from {Source} ignored: already provided by another service", key.ToUpperInvariant(), path, source);
                    continue;
                }

                var clone = value.DeepClone();
                // Routes that the gateway lets through without a token must not ask for one in the UI.
                if (HttpMethodKeys.Contains(key) && clone is JsonObject operation && PublicRoutes.IsPublic(path))
                    operation["security"] = new JsonArray();

                target[key] = clone;
            }
        }
    }

    private static void MergeComponents(JsonObject doc, JsonObject components)
    {
        if (doc["components"] is not JsonObject docComponents) return;

        foreach (var (category, items) in docComponents)
        {
            if (items is not JsonObject itemsObj) continue;

            if (components[category] is not JsonObject target)
            {
                target = new JsonObject();
                components[category] = target;
            }

            foreach (var (name, value) in itemsObj)
                if (value is not null && !target.ContainsKey(name)) target[name] = value.DeepClone();
        }
    }

    private static void MergeTags(JsonObject doc, JsonArray tags, HashSet<string> seen)
    {
        if (doc["tags"] is not JsonArray docTags) return;

        foreach (var tag in docTags)
            if (tag is JsonObject t && ReadString(t["name"]) is { } tagName && seen.Add(tagName))
                tags.Add(t.DeepClone());
    }

    /// <summary>Schema names present in both the merged document and this one with a different definition (name -> new unique name).</summary>
    private static Dictionary<string, string> FindSchemaCollisions(string source, JsonObject doc, JsonObject? mergedSchemas)
    {
        var renames = new Dictionary<string, string>();
        if (mergedSchemas is null || doc["components"]?["schemas"] is not JsonObject schemas) return renames;

        var prefix = new string(source.Where(char.IsLetterOrDigit).ToArray());
        if (prefix.Length == 0) prefix = "Service";
        prefix = char.ToUpperInvariant(prefix[0]) + prefix[1..];

        foreach (var (name, schema) in schemas)
            if (mergedSchemas.TryGetPropertyValue(name, out var existing) && !JsonNode.DeepEquals(existing, schema))
                renames[name] = prefix + name;

        return renames;
    }

    private static void RenameSchemas(JsonObject doc, Dictionary<string, string> renames)
    {
        if (renames.Count == 0) return;

        if (doc["components"]?["schemas"] is JsonObject schemas)
        {
            foreach (var (oldName, newName) in renames)
            {
                if (schemas.TryGetPropertyValue(oldName, out var node) && node is not null)
                {
                    schemas.Remove(oldName);
                    schemas[newName] = node;
                }
            }
        }

        RewriteRefs(doc, renames);
    }

    private static void RewriteRefs(JsonNode? node, Dictionary<string, string> renames)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (key == "$ref"
                        && ReadString(child) is { } reference
                        && reference.StartsWith(SchemaRefPrefix, StringComparison.Ordinal)
                        && renames.TryGetValue(reference[SchemaRefPrefix.Length..], out var newName))
                    {
                        obj[key] = SchemaRefPrefix + newName;
                    }
                    else
                    {
                        RewriteRefs(child, renames);
                    }
                }
                break;

            case JsonArray array:
                foreach (var child in array) RewriteRefs(child, renames);
                break;
        }
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject GatewayHealthItem() => new()
    {
        ["get"] = new JsonObject
        {
            ["tags"] = new JsonArray("Gateway"),
            ["summary"] = "Gateway health",
            ["security"] = new JsonArray(),
            ["responses"] = new JsonObject { ["200"] = new JsonObject { ["description"] = "The gateway is up." } },
        },
    };
}
