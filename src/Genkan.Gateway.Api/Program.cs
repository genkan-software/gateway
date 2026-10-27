using Genkan.Gateway.Api.Extensions;
using Genkan.Shared.Hosting;
using Genkan.Shared.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddGenkanLogging();

builder.Services.AddGatewayApplication(builder.Configuration);   // token validation, rate limiting, Swagger aggregation
builder.Services.AddGatewayProxy(builder.Configuration);         // YARP routes and clusters

var app = builder.Build();

app.UseCorrelationId();
app.UseGatewaySwagger();   // /swagger (combined UI of every service), only when Swagger:Enabled=true; answered before the token check
app.UseTokenValidation();
app.UseGatewayRateLimit("user");   // after the token check, so the bucket is the user's (X-User-Id)

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "gateway" }));
app.MapReverseProxy();

app.Run();
