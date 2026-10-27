using System.Globalization;
using System.Threading.RateLimiting;
using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Api.Background;
using Careeroc.Api.Endpoints;
using Careeroc.Api.Mcp;
using Careeroc.Api.Security;
using Careeroc.Infrastructure;
using Careeroc.Infrastructure.Configuration;
using Careeroc.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

EnvironmentFileLoader.LoadRootEnvFile();
var builder = WebApplication.CreateBuilder(args);

var useForwardedHeaders = ShouldUseForwardedHeaders(builder.Configuration);
var maxRequestBodyBytes = builder.Configuration.GetValue<long?>("Security:MaxRequestBodyBytes") ?? 8_388_608;

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodyBytes);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
AddCareerocOpenTelemetry(builder.Services, builder.Configuration, builder.Environment);
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddSingleton<ApiSecurityEventLogger>();
builder.Services.AddSingleton<ConfiguredUserAccess>();
builder.Services.AddHostedService<InboxSyncWorker>();
builder.Services.AddHostedService<DemoWorkspaceCleanupWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<CareerocMcpTools>();
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(
        options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
}
var apiRateLimitOptions =
    (builder.Configuration.GetSection(ApiRateLimitOptions.SectionName).Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions())
    .Normalize();
builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            TimeSpan? retryAfter = null;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue))
            {
                retryAfter = retryAfterValue;
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfterValue.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            var securityEventLogger = context.HttpContext.RequestServices.GetRequiredService<ApiSecurityEventLogger>();
            securityEventLogger.LogRateLimitRejected(
                context.HttpContext,
                ApiRateLimitPartitioner.ResolvePartitionKey(context.HttpContext),
                retryAfter);

            if (!context.HttpContext.Response.HasStarted)
            {
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Rate limit exceeded. Retry later." },
                    cancellationToken);
            }
        };
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            httpContext =>
            {
                if (!httpContext.Request.Path.StartsWithSegments("/api") &&
                    !PersonalAccessTokenMiddleware.AppliesTo(httpContext.Request.Path))
                {
                    return RateLimitPartition.GetNoLimiter("non-api");
                }

                if (ApiRateLimitPartitioner.IsOAuthCallback(httpContext.Request))
                {
                    return CreateFixedWindowPartition(
                        ApiRateLimitPartitioner.ResolvePartitionKey(httpContext),
                        apiRateLimitOptions.OAuthCallbackPermitLimit,
                        apiRateLimitOptions);
                }

                var partitionKey = ApiRateLimitPartitioner.ResolvePartitionKey(httpContext);
                var permitLimit = partitionKey.StartsWith("user:", StringComparison.Ordinal)
                    ? apiRateLimitOptions.UserPermitLimit
                    : apiRateLimitOptions.AnonymousPermitLimit;

                return CreateFixedWindowPartition(partitionKey, permitLimit, apiRateLimitOptions);
            });
    });
builder.Services.AddCors(
    options =>
        options.AddDefaultPolicy(
            policy =>
            {
                var allowedOrigins = ResolveAllowedOrigins(builder.Configuration, builder.Environment);

                if (allowedOrigins.Count > 0)
                {
                    policy
                        .WithOrigins(allowedOrigins.ToArray())
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            }));

var app = builder.Build();
AssertProductionDatabaseConfiguration(app.Configuration, app.Environment);
AssertProductionApiUrlConfiguration(app.Configuration, app.Environment);
AssertProductionApiAuthSecretConfiguration(app.Configuration, app.Environment);
AssertProductionInboxTokenProtectionKeyConfiguration(app.Configuration, app.Environment);
AssertProductionAgentRuntimeSecretConfiguration(app.Configuration, app.Environment);
AssertProductionUserAccessConfiguration(app.Configuration, app.Environment);

if (ShouldApplyMigrationsOnStartup(app.Configuration, app.Environment))
{
    await app.Services.ApplyCareerocMigrationsAsync();
}


await app.Services.SeedConfiguredAdminUsersAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}
app.UseHttpsRedirection();
app.UseCors();
app.Use(
    async (context, next) =>
    {
        var inboundCorrelationId = context.Request.Headers[CareerocTelemetry.CorrelationHeaderName].FirstOrDefault();
        using var correlationScope = CareerocTelemetry.BeginCorrelationScope(inboundCorrelationId);
        CareerocTelemetry.TagCurrentActivity();

        context.Response.OnStarting(
            () =>
            {
                if (!context.Response.Headers.ContainsKey(CareerocTelemetry.CorrelationHeaderName) &&
                    !string.IsNullOrWhiteSpace(CareerocTelemetry.CurrentCorrelationId))
                {
                    context.Response.Headers[CareerocTelemetry.CorrelationHeaderName] = CareerocTelemetry.CurrentCorrelationId;
                }

                return Task.CompletedTask;
            });

        await next(context);
    });
app.UseMiddleware<ApiUserContextMiddleware>();
app.UseWhen(
    context => PersonalAccessTokenMiddleware.AppliesTo(context.Request.Path),
    branch => branch.UseMiddleware<PersonalAccessTokenMiddleware>());
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("HealthCheck");
app.MapGet(
    "/health/ready",
    async (CareerocDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var databaseAvailable = await dbContext.Database.CanConnectAsync(cancellationToken);
        return databaseAvailable
            ? Results.Ok(new { status = "ok", database = "ok" })
            : Results.Json(
                new { status = "unavailable", database = "unavailable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }).WithName("ReadinessCheck");
app.MapAnalyticsEndpoints();
app.MapDashboardEndpoints();
app.MapInboxEndpoints();
app.MapProfileEndpoints();
app.MapOrchestratorEndpoints();
app.MapResumeEndpoints();
app.MapReviewEndpoints();
app.MapApplicationEndpoints();
app.MapContactEndpoints();
app.MapReminderEndpoints();
app.MapAdminEndpoints();
app.MapAgentAccessEndpoints();
app.MapMcp("/mcp");

if (app.Configuration.GetValue<bool>("Demo:Enabled"))
{
    app.MapDemoEndpoints();
}

app.Run();

static void AddCareerocOpenTelemetry(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
{
    var section = configuration.GetSection("Observability:OpenTelemetry");
    var explicitEndpoint =
        FirstNonEmpty(
            section["OtlpEndpoint"],
            configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    var enabled =
        section.GetValue<bool?>("Enabled") ??
        !string.IsNullOrWhiteSpace(explicitEndpoint);

    if (!enabled)
    {
        return;
    }

    var serviceName = FirstNonEmpty(section["ServiceName"], configuration["OTEL_SERVICE_NAME"]) ?? "Careeroc.Api";
    var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

    services
        .AddOpenTelemetry()
        .ConfigureResource(
            resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(
                    new Dictionary<string, object>
                    {
                        ["deployment.environment"] = environment.EnvironmentName,
                        ["careeroc.service"] = "api"
                    }))
        .WithTracing(
            tracing =>
            {
                tracing
                    .AddSource(CareerocTelemetry.ActivitySourceName)
                    .AddSource("Npgsql")
                    .AddAspNetCoreInstrumentation(
                        options =>
                        {
                            options.RecordException = true;
                            options.Filter = httpContext =>
                                !httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
                            options.EnrichWithHttpRequest = (activity, request) =>
                            {
                                activity.SetTag("careeroc.correlation_id", CareerocTelemetry.CurrentCorrelationId);
                                if (!string.IsNullOrWhiteSpace(request.HttpContext.User.Identity?.Name))
                                {
                                    activity.SetTag("enduser.id", request.HttpContext.User.Identity.Name);
                                }
                            };
                            options.EnrichWithHttpResponse = (activity, response) =>
                            {
                                activity.SetTag("http.response_content_length", response.ContentLength);
                                activity.SetTag("careeroc.correlation_id", CareerocTelemetry.CurrentCorrelationId);
                            };
                        })
                    .AddHttpClientInstrumentation(options => options.RecordException = true);

                tracing.AddOtlpExporter(
                    exporterOptions =>
                    {
                        // Match the web app and agent runtime: OTLP over HTTP unless gRPC is requested.
                        var protocol = FirstNonEmpty(
                            section["OtlpProtocol"],
                            configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]);
                        exporterOptions.Protocol = string.Equals(protocol, "grpc", StringComparison.OrdinalIgnoreCase)
                            ? OtlpExportProtocol.Grpc
                            : OtlpExportProtocol.HttpProtobuf;

                        if (!string.IsNullOrWhiteSpace(explicitEndpoint) &&
                            Uri.TryCreate(explicitEndpoint, UriKind.Absolute, out var endpoint))
                        {
                            // An endpoint set in code is used verbatim, so HTTP needs the signal path the SDK
                            // would otherwise add for OTEL_EXPORTER_OTLP_ENDPOINT.
                            exporterOptions.Endpoint =
                                exporterOptions.Protocol == OtlpExportProtocol.HttpProtobuf &&
                                !endpoint.AbsolutePath.EndsWith("/v1/traces", StringComparison.Ordinal)
                                    ? new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/v1/traces")
                                    : endpoint;
                        }
                    });
            });
}

static List<string> ResolveAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
{
    var values = new List<string>();

    AddOrigins(values, configuration.GetSection("Web:AllowedOrigins").Get<string[]>());
    AddOrigins(values, configuration["AUTH_URL"]);
    AddOrigins(values, configuration["NEXTAUTH_URL"]);

    if (environment.IsDevelopment())
    {
        AddOrigins(values, "http://localhost:3000");
        AddOrigins(values, "https://localhost:3000");
    }

    return values
        .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static void AssertProductionApiUrlConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    foreach (var (key, isInternal) in new[] { ("Careeroc:PublicApiBaseUrl", false), ("API_PUBLIC_BASE_URL", false), ("API_BASE_URL", true) })
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !ProductionUrlPolicy.IsAllowed(uri, isInternal))
        {
            throw new InvalidOperationException(
                $"{key} must be an absolute HTTPS URL outside Development"
                + (isInternal ? " unless it points at a private-network host." : "."));
        }
    }
}

static void AssertProductionDatabaseConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Careeroc")) ||
        HasConfiguredValues(configuration, "CAREEROC_CONNECTION_STRING", "CAREEROC_CONNECTION_STRING") ||
        HasConfiguredValues(configuration, "DATABASE_URL", "DATABASE_URL") ||
        HasConfiguredValues(configuration, "POSTGRES_URL", "POSTGRES_URL") ||
        HasConfiguredValues(configuration, "POSTGRESQL_URL", "POSTGRESQL_URL"))
    {
        return;
    }

    throw new InvalidOperationException(
        "careeroc database connection string is required outside Development. Set ConnectionStrings__Careeroc, CAREEROC_CONNECTION_STRING, DATABASE_URL, POSTGRES_URL, or POSTGRESQL_URL.");
}

static void AssertProductionApiAuthSecretConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    var secret =
        FirstNonEmpty(
            configuration["Careeroc:ApiAuthSecret"],
            configuration["CAREEROC_API_AUTH_SECRET"]);

    if (string.IsNullOrWhiteSpace(secret))
    {
        throw new InvalidOperationException(
            "careeroc API request signing secret is required outside Development. Set CAREEROC_API_AUTH_SECRET.");
    }

    if (secret is "careeroc-dev-api-auth-secret-change-me" or "careeroc-dev-auth-secret-change-me" ||
        secret.Length < 32)
    {
        throw new InvalidOperationException(
            "careeroc API request signing secret must be at least 32 characters and must not use development defaults outside Development.");
    }

    AssertNotComposeFallbackSecret(configuration, secret, "CAREEROC_API_AUTH_SECRET");
}

// The runtime refuses unsigned calls, so a missing secret would silently turn every review into the
// deterministic fallback; failing at startup makes the mistake visible.
static void AssertProductionAgentRuntimeSecretConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (environment.IsDevelopment() || !configuration.GetValue<bool>("AgentRuntime:Enabled"))
    {
        return;
    }

    var secret = configuration["AgentRuntime:SharedSecret"];
    if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
    {
        throw new InvalidOperationException(
            "AgentRuntime:SharedSecret must be at least 32 characters when the agent runtime is enabled outside Development.");
    }

    AssertNotComposeFallbackSecret(configuration, secret, "AgentRuntime__SharedSecret");
}

// docker-compose.yml ships public fallback secrets so the demo starts with no setup. They are only
// safe while the allowlist admits nothing but fictional demo visitors.
static void AssertNotComposeFallbackSecret(IConfiguration configuration, string secret, string name)
{
    var access = UserAccessSettings.FromConfiguration(configuration);
    var demoDomain = configuration["Demo:EmailDomain"] ?? new DemoOptions().EmailDomain;
    var demoOnly = configuration.GetValue<bool>("Demo:Enabled") &&
                   access.AllowedEmails.Count == 0 &&
                   access.AllowedDomains.SetEquals([demoDomain]);
    if (!demoOnly && secret.StartsWith("local-compose-only-", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"{name} still uses the public fallback from docker-compose.yml. Set your own random value before allowing real accounts.");
    }
}

static void AssertProductionInboxTokenProtectionKeyConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    var secret =
        FirstNonEmpty(
            configuration["Security:TokenProtectionKey"],
            configuration["CAREEROC_TOKEN_PROTECTION_KEY"]);

    if (string.IsNullOrWhiteSpace(secret))
    {
        throw new InvalidOperationException(
            "careeroc inbox token protection key is required outside Development. Set Security:TokenProtectionKey or CAREEROC_TOKEN_PROTECTION_KEY. Do not reuse AUTH_SECRET or CAREEROC_API_AUTH_SECRET.");
    }

    if (secret is "careeroc-dev-token-protection-key-change-me" ||
        secret.Length < 32)
    {
        throw new InvalidOperationException(
            "careeroc inbox token protection key must be at least 32 characters and must not use development defaults outside Development.");
    }

    AssertNotComposeFallbackSecret(configuration, secret, "CAREEROC_TOKEN_PROTECTION_KEY");
}

static void AssertProductionUserAccessConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (environment.IsDevelopment() || IsExplicitAllowAllUsers(configuration))
    {
        return;
    }

    if (HasConfiguredValues(configuration, "Access:AllowedEmails", "ACCESS_ALLOWED_EMAILS") ||
        HasConfiguredValues(configuration, "Access:AllowedDomains", "ACCESS_ALLOWED_DOMAINS"))
    {
        return;
    }

    throw new InvalidOperationException(
        "careeroc requires an explicit production access policy. Set ACCESS_ALLOWED_EMAILS or ACCESS_ALLOWED_DOMAINS, or set Access:AllowAllUsers=true only for an intentionally open deployment.");
}

static string FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return string.Empty;
}

static bool IsExplicitAllowAllUsers(IConfiguration configuration)
{
    return configuration.GetValue<bool>("Access:AllowAllUsers") ||
           configuration.GetValue<bool>("ACCESS_ALLOW_ALL_USERS");
}

static bool HasConfiguredValues(IConfiguration configuration, string sectionKey, string environmentKey)
{
    var sectionValues = configuration.GetSection(sectionKey).Get<string[]>() ?? [];
    return sectionValues.Any(value => !string.IsNullOrWhiteSpace(value)) ||
           !string.IsNullOrWhiteSpace(configuration[sectionKey]) ||
           !string.IsNullOrWhiteSpace(configuration[environmentKey]);
}

static bool ShouldUseForwardedHeaders(IConfiguration configuration)
{
    return configuration.GetValue<bool>("ForwardedHeaders:Enabled") ||
           string.Equals(
               Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"),
               "true",
               StringComparison.OrdinalIgnoreCase);
}

static bool ShouldApplyMigrationsOnStartup(IConfiguration configuration, IHostEnvironment environment)
{
    return configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup") ??
           environment.IsDevelopment();
}

static void AddOrigins(List<string> values, params string?[]? configuredOrigins)
{
    if (configuredOrigins is null)
    {
        return;
    }

    foreach (var origin in configuredOrigins)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            continue;
        }

        values.AddRange(
            origin
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}

static RateLimitPartition<string> CreateFixedWindowPartition(
    string partitionKey,
    int permitLimit,
    ApiRateLimitOptions options)
{
    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(options.WindowSeconds),
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
}

public partial class Program;
