using System.Net;
using System.Text.Json;
using Genkan.Shared.Configuration;
using Genkan.Gateway.Application.Services;
using Microsoft.Extensions.Options;

namespace Genkan.Gateway.Api.Extensions;

public static class SwaggerEndpointExtensions
{
    /// <summary>
    /// When Swagger:Enabled is true, serves the combined Swagger UI on GET /swagger and the merged document on GET /swagger/openapi.json.
    /// Call it before the token check: both URLs are public (the UI itself sends the bearer token on "Try it out").
    /// When it is false nothing is registered and /swagger behaves like any other unknown route.
    /// </summary>
    public static IApplicationBuilder UseGatewaySwagger(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<SwaggerOptions>>().Value;
        if (!options.Enabled) return app;

        app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Genkan.Gateway.Swagger")
            .LogInformation("Combined Swagger UI enabled on /swagger ({Count} source(s))", options.Sources.Count);

        app.Map("/swagger", branch => branch.Run(ctx => HandleAsync(ctx, options)));
        return app;
    }

    private static async Task HandleAsync(HttpContext ctx, SwaggerOptions options)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method))
        {
            ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        // Inside the "/swagger" branch Path is what follows it: "" or "/" for the UI, "/openapi.json" for the document.
        switch ((ctx.Request.Path.Value ?? "").TrimEnd('/'))
        {
            case "":
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(Page(options.Title, $"{ctx.Request.PathBase}/openapi.json"), ctx.RequestAborted);
                break;

            case "/openapi.json":
            {
                var json = await ctx.RequestServices.GetRequiredService<OpenApiAggregator>().GetAsync(ctx.RequestAborted);
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-store";
                await ctx.Response.WriteAsync(json, ctx.RequestAborted);
                break;
            }

            default:
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                break;
        }
    }

    // Swagger UI is loaded from a CDN, so the browser (not the gateway) needs internet access.
    private static string Page(string title, string specUrl) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{WebUtility.HtmlEncode(title)}}</title>
          <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css">
        </head>
        <body>
          <div id="swagger-ui"></div>
          <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
          <script>
            window.ui = SwaggerUIBundle(
              {
                url: {{JsonSerializer.Serialize(specUrl)}},
                dom_id: "#swagger-ui",
                deepLinking: true,
                persistAuthorization: true
              }
            );
          </script>
        </body>
        </html>
        """;
}
