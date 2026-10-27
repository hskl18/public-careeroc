using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Careeroc.Application.Services;

public sealed record DemoWorkspaceResult(bool Created, int EmailsImported, int ApplicationsCreated, int RemindersCreated);

public sealed class DemoWorkspaceUnavailableException(string message) : InvalidOperationException(message);

public sealed class DemoWorkspaceCapacityException()
    : InvalidOperationException("The demo is busy right now. Try again in a few minutes.");

// Gives every demo visitor a private, short-lived workspace built by the real sync and extraction pipeline.
public sealed class DemoWorkspaceService
{
    public const string PersonaName = "Alex Rivera";

    private readonly ICareerocDbContext _dbContext;
    private readonly EmailSyncService _emailSyncService;
    private readonly EmailIntelligenceService _emailIntelligenceService;
    private readonly ResumeIntelligenceService _resumeIntelligenceService;
    private readonly DemoOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DemoWorkspaceService> _logger;

    public DemoWorkspaceService(
        ICareerocDbContext dbContext,
        EmailSyncService emailSyncService,
        EmailIntelligenceService emailIntelligenceService,
        ResumeIntelligenceService resumeIntelligenceService,
        IOptions<DemoOptions> options,
        IClock clock,
        ILogger<DemoWorkspaceService> logger)
    {
        _dbContext = dbContext;
        _emailSyncService = emailSyncService;
        _emailIntelligenceService = emailIntelligenceService;
        _resumeIntelligenceService = resumeIntelligenceService;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DemoWorkspaceResult> EnsureWorkspaceAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new DemoWorkspaceUnavailableException("The demo is not enabled on this deployment.");
        }

        var email = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        if (!_options.IsDemoEmail(email))
        {
            throw new DemoWorkspaceUnavailableException("Only demo identities can open a demo workspace.");
        }

        var user = await _dbContext.Users
            .Include(candidate => candidate.InboxAccounts)
            .FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user?.InboxAccounts.Any(account => account.Provider == ApplicationDefaults.DemoInboxProvider) == true)
        {
            return new DemoWorkspaceResult(false, 0, 0, 0);
        }

        var now = _clock.UtcNow;
        var demoEmailSuffix = $"@{_options.EmailDomain}";
        var createdLastHour = await _dbContext.Users.CountAsync(
            candidate => candidate.Email.EndsWith(demoEmailSuffix) && candidate.CreatedAtUtc >= now.AddHours(-1),
            cancellationToken);
        if (createdLastHour >= _options.MaxWorkspacesPerHour)
        {
            throw new DemoWorkspaceCapacityException();
        }

        if (user is null)
        {
            user = new User
            {
                Email = email,
                Name = PersonaName,
                Location = "San Francisco, CA",
                TargetRole = "Backend Engineer",
                CreatedAtUtc = now
            };
            await _dbContext.Users.AddAsync(user, cancellationToken);
        }

        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.DemoInboxProvider,
            EmailAddress = email,
            CreatedAtUtc = now
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "demo",
                ConnectionStatus = "Connected",
                NextStep = "This fictional inbox belongs to a demo workspace that is deleted after "
                    + $"{_options.WorkspaceLifetimeHours} hours."
            });
        await _dbContext.InboxAccounts.AddAsync(inboxAccount, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var sync = await _emailSyncService.RunOnceAsync(email, cancellationToken);
        var processing = await _emailIntelligenceService.ProcessPendingUntilIdleAsync(
            100,
            10,
            email,
            cancellationToken);
        await _resumeIntelligenceService.CreateResumeAsync(
            new CreateResumeRequest("Alex Rivera - Backend Engineer", "text", DemoPersonaResume.Text),
            email,
            cancellationToken);

        _logger.LogInformation(
            "Created demo workspace with {EmailCount} emails and {ApplicationCount} applications.",
            sync.EmailsCreated,
            processing.ApplicationsCreated);

        return new DemoWorkspaceResult(
            true,
            sync.EmailsCreated,
            processing.ApplicationsCreated,
            processing.RemindersCreated);
    }

    public async Task<int> DeleteExpiredWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _clock.UtcNow.AddHours(-Math.Max(1, _options.WorkspaceLifetimeHours));
        var demoEmailSuffix = $"@{_options.EmailDomain}";

        // Child rows go with the user through ON DELETE CASCADE; these logs only reference the address.
        await _dbContext.AuditEvents
            .Where(entry => entry.UserEmail != null && entry.UserEmail.EndsWith(demoEmailSuffix) && entry.OccurredAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        await _dbContext.ModelCallEvents
            .Where(entry => entry.UserEmail != null && entry.UserEmail.EndsWith(demoEmailSuffix) && entry.OccurredAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return await _dbContext.Users
            .Where(user => user.Email.EndsWith(demoEmailSuffix) && user.CreatedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

public static class DemoPersonaResume
{
    public const string Text = """
        Alex Rivera
        San Francisco, CA | alex@demo.careeroc.example | github.com/alexrivera-demo

        EDUCATION
        Westbrook University, B.S. Computer Science, Jun 2026

        WORK EXPERIENCE
        Harbor Metrics Software Engineer Intern San Francisco, CA Jun 2025 - Sep 2025
        • Built a streaming ingestion service in Go that processed 40k events per second with exactly-once writes to Postgres.
        • Cut p95 dashboard query latency from 1.8 s to 220 ms with materialized rollups and query-plan reviews.
        Cedar Labs Backend Engineer Intern Oakland, CA Jun 2024 - Sep 2024
        • Designed REST and gRPC APIs for a scheduling product used by 300 clinics.
        • Added contract tests and canary deploys that removed a class of breaking API releases.

        TECHNICAL PROJECTS
        Inbox Copilot | 2026
        • Multi-agent assistant that turns recruiting email into a reviewed application pipeline with evidence and human approval gates.
        Raft Playground | 2025
        • Visual Raft consensus simulator with fault injection, used in a distributed systems course.

        SKILLS
        Go, C#, TypeScript, Python, PostgreSQL, Redis, Kafka, Docker, Kubernetes, AWS, OpenTelemetry
        """;
}
