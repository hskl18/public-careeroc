using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Infrastructure.Agents;
using Careeroc.Infrastructure.Demo;
using Careeroc.Infrastructure.Email;
using Careeroc.Infrastructure.Google;
using Careeroc.Infrastructure.Persistence;
using Careeroc.Infrastructure.Resume;
using Careeroc.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Careeroc.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = CareerocConnectionString.Resolve(configuration);

        services.AddDbContext<CareerocDbContext>(
            options =>
                options
                    .UseNpgsql(connectionString)
                    .UseSnakeCaseNamingConvention());
        services.Configure<DemoOptions>(configuration.GetSection(DemoOptions.SectionName));
        services.AddOptions<AgentRuntimeOptions>()
            .Bind(configuration.GetSection(AgentRuntimeOptions.SectionName))
            .PostConfigure(
                options =>
                {
                    options.TimeoutSeconds = Math.Clamp(options.TimeoutSeconds, 5, 300);
                    if (string.IsNullOrWhiteSpace(options.BaseUrl))
                    {
                        options.BaseUrl = "http://localhost:8123";
                    }
                });

        services.AddOptions<GmailOptions>()
            .Bind(configuration.GetSection("Google"))
            .PostConfigure(
                options =>
                {
                    options.SearchPageLimit = Math.Clamp(options.SearchPageLimit, 1, 20);
                    options.SearchMessageLimit = Math.Clamp(options.SearchMessageLimit, 1, 1000);
                    options.ThreadFetchLimit = Math.Clamp(options.ThreadFetchLimit, 0, 200);
                    options.HistoryPageLimit = Math.Clamp(options.HistoryPageLimit, 1, 50);
                    options.HistoryMessageLimit = Math.Clamp(options.HistoryMessageLimit, 1, 1000);
                    options.BodyCharacterLimit = Math.Clamp(options.BodyCharacterLimit, 1000, 100000);
                });
        services.AddOptions<OllamaEmailExtractionOptions>()
            .Bind(configuration.GetSection("Ollama"))
            .PostConfigure(
                options =>
                {
                    options.BaseUrl = ResolveOllamaString(
                        options.BaseUrl,
                        null,
                        configuration["ollama_api"]);
                    options.Model = ResolveOllamaString(
                        options.Model,
                        "gemma4:31b")!;
                    options.ApiKey = ResolveOllamaString(options.ApiKey);

                    if (options.TimeoutSeconds <= 0)
                    {
                        options.TimeoutSeconds = 90;
                    }

                    options.MaxConcurrentRequests = Math.Clamp(options.MaxConcurrentRequests, 1, 64);
                    options.RetryCount = Math.Clamp(options.RetryCount, 0, 5);
                    options.RetryDelayMilliseconds = Math.Clamp(options.RetryDelayMilliseconds, 0, 5000);

                    if (options.MinimumConfidence <= 0)
                    {
                        options.MinimumConfidence = 0.60m;
                    }

                    if (options.GuidanceMaxCharacters <= 0)
                    {
                        options.GuidanceMaxCharacters = 16000;
                    }

                    if (options.FailureCircuitBreakerThreshold < 0)
                    {
                        options.FailureCircuitBreakerThreshold = 5;
                    }

                    if (options.FailureCircuitBreakerCooldownSeconds < 0)
                    {
                        options.FailureCircuitBreakerCooldownSeconds = 300;
                    }

                    options.Enabled = ResolveExplicitOllamaEnabled(configuration, options.Enabled);
                });
        services.AddScoped<ICareerocDbContext>(serviceProvider => serviceProvider.GetRequiredService<CareerocDbContext>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<OllamaModelCircuitBreaker>();
        services.AddSingleton<OllamaHttpConcurrencyLimiter>();
        services.AddTransient<OllamaHttpResilienceHandler>();
        services.AddHttpClient<IGmailInboxClient, GmailInboxClient>();
        services.AddHttpClient<ICareerAgentGraphClient, CareerAgentGraphHttpClient>(
            (serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AgentRuntimeOptions>>().Value;
                httpClient.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });
        services.AddHttpClient<OllamaRecruitingEmailParser>(ConfigureOllamaHttpClient)
            .AddHttpMessageHandler<OllamaHttpResilienceHandler>();
        services.AddHttpClient<OllamaReviewEvidenceModel>(ConfigureOllamaHttpClient)
            .AddHttpMessageHandler<OllamaHttpResilienceHandler>();
        services.AddHttpClient<OllamaResumeEvaluationModel>(ConfigureOllamaHttpClient)
            .AddHttpMessageHandler<OllamaHttpResilienceHandler>();
        services.AddHttpClient<OllamaResumeVisionTextExtractor>(ConfigureOllamaHttpClient)
            .AddHttpMessageHandler<OllamaHttpResilienceHandler>();
        services.AddScoped<DemoInboxEmailSource>();
        services.AddScoped<IInboxEmailSource, ConfiguredInboxEmailSource>();
        services.AddScoped<IInboxAttachmentDownloader, ConfiguredInboxAttachmentDownloader>();
        services.AddSingleton<IInboxAccountTokenProtector, ConfiguredInboxAccountTokenProtector>();
        services.AddScoped<ILeaseManager, DatabaseLeaseManager>();
        services.AddScoped<IEmailProcessingQueue, DatabaseEmailProcessingQueue>();
        services.AddScoped<IModelCallEventRecorder, DatabaseModelCallEventRecorder>();
        services.AddSingleton<IRecruitingEmailGuidanceStore, MarkdownRecruitingEmailGuidanceStore>();
        services.AddScoped<HeuristicRecruitingEmailParser>();
        services.AddScoped<IExplicitDueDateExtractor, HeuristicDueDateExtractor>();
        services.AddScoped<IRecruitingEmailParser, OllamaRecruitingEmailParser>();
        services.AddScoped<IReviewEvidenceModel>(serviceProvider => serviceProvider.GetRequiredService<OllamaReviewEvidenceModel>());
        services.AddScoped<IResumeEvaluationModel>(serviceProvider => serviceProvider.GetRequiredService<OllamaResumeEvaluationModel>());
        services.AddScoped<IResumeVisionTextExtractor>(serviceProvider => serviceProvider.GetRequiredService<OllamaResumeVisionTextExtractor>());

        return services;
    }

    private static string? ResolveOllamaString(
        string? configuredValue,
        string? defaultValue = null,
        params string?[] legacyValues)
    {
        foreach (var value in legacyValues.Prepend(configuredValue))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return defaultValue;
    }

    private static bool ResolveExplicitOllamaEnabled(IConfiguration configuration, bool configuredValue)
    {
        return ResolveBool(
            configuration,
            configuredValue,
            "Ollama:Enabled");
    }

    private static void ConfigureOllamaHttpClient(IServiceProvider serviceProvider, HttpClient httpClient)
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<OllamaEmailExtractionOptions>>()
            .Value;

        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }

    private static bool ResolveBool(IConfiguration configuration, bool configuredValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (bool.TryParse(configuration[key], out var parsed))
            {
                return parsed;
            }
        }

        return configuredValue;
    }

    public static async Task ApplyCareerocMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
