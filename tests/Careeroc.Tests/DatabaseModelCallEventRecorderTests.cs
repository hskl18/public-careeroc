using Careeroc.Application.Models;
using Careeroc.Infrastructure.Email;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Careeroc.Tests;

public sealed class DatabaseModelCallEventRecorderTests
{
    [Fact]
    public async Task RecordAsync_PersistsModelCallEvent()
    {
        var services = new ServiceCollection();
        var databaseName = $"{nameof(DatabaseModelCallEventRecorderTests)}-{Guid.NewGuid()}";
        services.AddDbContext<CareerocDbContext>(
            options => options.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();
        var logger = new CapturingLogger<DatabaseModelCallEventRecorder>();
        var recorder = new DatabaseModelCallEventRecorder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger);
        var inboxAccountId = Guid.NewGuid();
        var inboxEmailId = Guid.NewGuid();

        await recorder.RecordAsync(
            new ModelCallEventRecord(
                new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero),
                "ollama",
                "gemma4:26b",
                "workflow_extraction",
                "success",
                1234,
                "USER@EXAMPLE.COM",
                inboxAccountId,
                inboxEmailId,
                "run-1",
                PromptTokens: 80,
                CompletionTokens: 20,
                TotalTokens: 100,
                EstimatedCostUsd: 0.001m,
                StatusCode: 200,
                CorrelationId: "corr-1"));

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        Assert.Null(logger.LastException);
        var persisted = await dbContext.ModelCallEvents.SingleAsync();

        Assert.Equal("user@example.com", persisted.UserEmail);
        Assert.Equal("ollama", persisted.Provider);
        Assert.Equal("gemma4:26b", persisted.Model);
        Assert.Equal("workflow_extraction", persisted.Purpose);
        Assert.Equal("success", persisted.Outcome);
        Assert.Equal(inboxAccountId, persisted.InboxAccountId);
        Assert.Equal(inboxEmailId, persisted.InboxEmailId);
        Assert.Equal("run-1", persisted.RunId);
        Assert.Equal("corr-1", persisted.CorrelationId);
        Assert.Equal(100, persisted.TotalTokens);
        Assert.Equal(0.001m, persisted.EstimatedCostUsd);
        Assert.Equal(1234, persisted.LatencyMs);
        Assert.Equal(200, persisted.StatusCode);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LastException = exception;
        }
    }
}
