using Careeroc.Application;
using System.Data;
using Careeroc.Application.Abstractions;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Careeroc.Infrastructure.Email;

public sealed class DatabaseEmailProcessingQueue : IEmailProcessingQueue
{
    private static readonly TimeSpan DefaultProcessingLockTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMinutes(1);
    private const int DefaultMaxActiveJobs = 5000;
    private const int DefaultMaxAttempts = 3;

    private readonly CareerocDbContext _dbContext;
    private readonly IClock _clock;
    private readonly TimeSpan _processingLockTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly int _maxActiveJobs;
    private readonly int _maxAttempts;

    public DatabaseEmailProcessingQueue(
        CareerocDbContext dbContext,
        IClock clock,
        IConfiguration? configuration = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _processingLockTimeout = ResolveProcessingLockTimeout(configuration);
        _retryDelay = ResolveRetryDelay(configuration);
        _maxActiveJobs = ResolveMaxActiveJobs(configuration);
        _maxAttempts = ResolveMaxAttempts(configuration);
    }

    public async Task<int> EnqueueAsync(Guid inboxEmailId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.EmailProcessingJobs
            .AnyAsync(job => job.InboxEmailId == inboxEmailId, cancellationToken);

        if (exists)
        {
            return 0;
        }

        if (_maxActiveJobs > 0)
        {
            var activeJobs = await _dbContext.EmailProcessingJobs
                .CountAsync(
                    job =>
                        job.Status == EmailProcessingStatus.Pending ||
                        job.Status == EmailProcessingStatus.Processing,
                    cancellationToken);
            if (activeJobs >= _maxActiveJobs)
            {
                return 0;
            }
        }

        _dbContext.EmailProcessingJobs.Add(new EmailProcessingJob
        {
            InboxEmailId = inboxEmailId,
            Status = EmailProcessingStatus.Pending,
            RunId = inboxEmailId.ToString("N"),
            CorrelationId = CareerocTelemetry.CurrentCorrelationId,
            AvailableAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            return 0;
        }

        return 1;
    }

    public async Task<IReadOnlyList<EmailProcessingJob>> DequeuePendingBatchAsync(
        int maxBatchSize,
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        if (maxBatchSize <= 0)
        {
            return [];
        }

        await FailJobsThatTimedOutOnTheirLastAttemptAsync(cancellationToken);
        if (IsPostgresProvider())
        {
            return await DequeuePendingBatchPostgresAsync(maxBatchSize, userEmail, cancellationToken);
        }

        return await DequeuePendingBatchFallbackAsync(maxBatchSize, userEmail, cancellationToken);
    }

    // A worker that dies on a job's last attempt leaves it in Processing, and neither claim query
    // picks it up again, so it would never reach Failed or the admin retry list.
    private async Task FailJobsThatTimedOutOnTheirLastAttemptAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var staleProcessingCutoff = now.Subtract(_processingLockTimeout);
        var exhausted = await _dbContext.EmailProcessingJobs
            .Where(
                job =>
                    job.Status == EmailProcessingStatus.Processing &&
                    job.Attempts >= _maxAttempts &&
                    (!job.LockedAtUtc.HasValue || job.LockedAtUtc <= staleProcessingCutoff))
            .ToListAsync(cancellationToken);
        if (exhausted.Count == 0)
        {
            return;
        }

        foreach (var job in exhausted)
        {
            job.Status = EmailProcessingStatus.Failed;
            job.LastError = "Timed out on its last attempt.";
            job.DeadLetterReason = $"Exceeded max attempts ({_maxAttempts}).";
            job.DeadLetteredAtUtc = now;
            job.LockedAtUtc = null;
            job.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EmailProcessingJob>> DequeuePendingBatchFallbackAsync(
        int maxBatchSize,
        string? userEmail,
        CancellationToken cancellationToken)
    {
        // Keep this fallback in lockstep with DequeuePendingBatchPostgresAsync. Tests use
        // this path, while production Postgres uses FOR UPDATE SKIP LOCKED below.
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var now = _clock.UtcNow;
        var staleProcessingCutoff = now.Subtract(_processingLockTimeout);
        var query = _dbContext.EmailProcessingJobs
            .Where(
                job =>
                    job.Attempts < _maxAttempts &&
                    ((job.Status == EmailProcessingStatus.Pending && job.AvailableAtUtc <= now) ||
                     (job.Status == EmailProcessingStatus.Processing &&
                      (!job.LockedAtUtc.HasValue || job.LockedAtUtc <= staleProcessingCutoff))))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(resolvedUserEmail))
        {
            query = query.Where(job => job.InboxEmail.InboxAccount.User.Email == resolvedUserEmail);
        }

        var jobs = await query
            .OrderBy(job => job.Status)
            .ThenBy(job => job.CreatedAtUtc)
            .Take(maxBatchSize)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return jobs;
        }

        foreach (var job in jobs)
        {
            if (job.Status == EmailProcessingStatus.Processing)
            {
                job.LastError = job.LockedAtUtc.HasValue
                    ? $"Recovered stale processing lock from {job.LockedAtUtc:O}."
                    : "Recovered processing job with no lock timestamp.";
            }

            job.Status = EmailProcessingStatus.Processing;
            job.Attempts += 1;
            job.LockedAtUtc = now;
            job.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return jobs;
    }

    private async Task<IReadOnlyList<EmailProcessingJob>> DequeuePendingBatchPostgresAsync(
        int maxBatchSize,
        string? userEmail,
        CancellationToken cancellationToken)
    {
        // Keep this production claim query in lockstep with DequeuePendingBatchFallbackAsync.
        // Any retry, stale-lock, or max-attempt change must be mirrored in both paths.
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var now = _clock.UtcNow;
        var staleProcessingCutoff = now.Subtract(_processingLockTimeout);
        var claimedJobIds = new List<Guid>();
        var connection = _dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;

        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                WITH candidate_jobs AS (
                    SELECT job.id
                    FROM email_processing_jobs AS job
                    INNER JOIN inbox_emails AS email ON email.id = job.inbox_email_id
                    INNER JOIN inbox_accounts AS account ON account.id = email.inbox_account_id
                    INNER JOIN users AS owner ON owner.id = account.user_id
                    WHERE (
                        (job.attempts < @max_attempts AND job.status = @pending_status AND job.available_at_utc <= @now) OR
                        (job.attempts < @max_attempts AND job.status = @processing_status AND (job.locked_at_utc IS NULL OR job.locked_at_utc <= @stale_processing_cutoff))
                    )
                    AND (@user_email IS NULL OR owner.email = @user_email)
                    ORDER BY job.status, job.created_at_utc
                    LIMIT @max_batch_size
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE email_processing_jobs AS job
                SET
                    status = @processing_status,
                    attempts = job.attempts + 1,
                    locked_at_utc = @now,
                    updated_at_utc = @now,
                    last_error = CASE
                        WHEN job.status = @processing_status AND job.locked_at_utc IS NOT NULL
                            THEN 'Recovered stale processing lock from ' || job.locked_at_utc::text || '.'
                        WHEN job.status = @processing_status
                            THEN 'Recovered processing job with no lock timestamp.'
                        ELSE job.last_error
                    END
                FROM candidate_jobs
                WHERE job.id = candidate_jobs.id
                RETURNING job.id;
                """;

            AddParameter(command, "pending_status", (int)EmailProcessingStatus.Pending);
            AddParameter(command, "processing_status", (int)EmailProcessingStatus.Processing);
            AddParameter(command, "now", now);
            AddParameter(command, "stale_processing_cutoff", staleProcessingCutoff);
            AddParameter(command, "max_attempts", _maxAttempts);
            AddParameter(
                command,
                "user_email",
                string.IsNullOrWhiteSpace(resolvedUserEmail) ? DBNull.Value : resolvedUserEmail,
                DbType.String);
            AddParameter(command, "max_batch_size", maxBatchSize);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimedJobIds.Add(reader.GetGuid(0));
            }

            await reader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }

        if (claimedJobIds.Count == 0)
        {
            return [];
        }

        var jobs = await _dbContext.EmailProcessingJobs
            .Where(job => claimedJobIds.Contains(job.Id))
            .ToListAsync(cancellationToken);
        var jobsById = jobs.ToDictionary(job => job.Id);

        return claimedJobIds
            .Where(jobsById.ContainsKey)
            .Select(id => jobsById[id])
            .ToList();
    }

    public async Task<int> CountReadyAsync(
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var now = _clock.UtcNow;
        var staleProcessingCutoff = now.Subtract(_processingLockTimeout);
        var query = _dbContext.EmailProcessingJobs
            .Where(
                job =>
                    job.Attempts < _maxAttempts &&
                    ((job.Status == EmailProcessingStatus.Pending && job.AvailableAtUtc <= now) ||
                     (job.Status == EmailProcessingStatus.Processing &&
                      (!job.LockedAtUtc.HasValue || job.LockedAtUtc <= staleProcessingCutoff))));

        if (!string.IsNullOrWhiteSpace(resolvedUserEmail))
        {
            query = query.Where(job => job.InboxEmail.InboxAccount.User.Email == resolvedUserEmail);
        }

        return await query.CountAsync(cancellationToken);
    }

    private static string? ResolveUserEmail(string? userEmail)
    {
        return string.IsNullOrWhiteSpace(userEmail)
            ? null
            : userEmail.Trim().ToLowerInvariant();
    }

    private bool IsPostgresProvider()
    {
        return string.Equals(_dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            return string.Equals(postgresException.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal);
        }

        return exception.InnerException?.GetType().Name == "SqliteException" &&
               exception.InnerException.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan ResolveProcessingLockTimeout(IConfiguration? configuration)
    {
        var configuredSeconds = configuration?.GetValue<int?>("EmailProcessing:ProcessingLockTimeoutSeconds");
        if (!configuredSeconds.HasValue)
        {
            return DefaultProcessingLockTimeout;
        }

        return TimeSpan.FromSeconds(Math.Clamp(configuredSeconds.Value, 60, 7200));
    }

    private static TimeSpan ResolveRetryDelay(IConfiguration? configuration)
    {
        var configuredSeconds = configuration?.GetValue<int?>("EmailProcessing:RetryDelaySeconds");
        if (!configuredSeconds.HasValue)
        {
            return DefaultRetryDelay;
        }

        return TimeSpan.FromSeconds(Math.Clamp(configuredSeconds.Value, 1, 3600));
    }

    private static int ResolveMaxActiveJobs(IConfiguration? configuration)
    {
        var configuredLimit = configuration?.GetValue<int?>("EmailProcessing:MaxActiveJobs");
        if (!configuredLimit.HasValue)
        {
            return DefaultMaxActiveJobs;
        }

        return configuredLimit.Value <= 0 ? 0 : Math.Clamp(configuredLimit.Value, 1, 100000);
    }

    private static int ResolveMaxAttempts(IConfiguration? configuration)
    {
        var configuredLimit = configuration?.GetValue<int?>("EmailProcessing:MaxAttempts");
        if (!configuredLimit.HasValue)
        {
            return DefaultMaxAttempts;
        }

        return Math.Clamp(configuredLimit.Value, 1, 100);
    }

    private static void AddParameter(IDbCommand command, string name, object value, DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        if (dbType.HasValue)
        {
            parameter.DbType = dbType.Value;
        }

        command.Parameters.Add(parameter);
    }

    public async Task MarkCompletedAsync(
        Guid jobId,
        CancellationToken cancellationToken = default,
        DateTimeOffset? lockedAtUtc = null)
    {
        var job = await FindJobForLeaseUpdateAsync(jobId, lockedAtUtc, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = EmailProcessingStatus.Completed;
        job.CompletedAtUtc = _clock.UtcNow;
        job.LockedAtUtc = null;
        job.LastError = null;
        job.UpdatedAtUtc = _clock.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeferAsync(
        Guid jobId,
        TimeSpan delay,
        CancellationToken cancellationToken = default,
        DateTimeOffset? lockedAtUtc = null)
    {
        var job = await FindJobForLeaseUpdateAsync(jobId, lockedAtUtc, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = EmailProcessingStatus.Pending;
        job.Attempts = Math.Max(0, job.Attempts - 1);
        job.LockedAtUtc = null;
        job.AvailableAtUtc = _clock.UtcNow.Add(delay);
        job.UpdatedAtUtc = _clock.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid jobId,
        string error,
        CancellationToken cancellationToken = default,
        DateTimeOffset? lockedAtUtc = null)
    {
        var job = await FindJobForLeaseUpdateAsync(jobId, lockedAtUtc, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.LastError = FormatFailure(error);
        job.LockedAtUtc = null;
        job.UpdatedAtUtc = _clock.UtcNow;

        if (job.Attempts >= _maxAttempts)
        {
            job.Status = EmailProcessingStatus.Failed;
            job.DeadLetterReason = $"Exceeded max attempts ({_maxAttempts}).";
            job.DeadLetteredAtUtc = _clock.UtcNow;
        }
        else
        {
            job.Status = EmailProcessingStatus.Pending;
            job.AvailableAtUtc = _clock.UtcNow.Add(_retryDelay);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EmailProcessingJob?> FindJobForLeaseUpdateAsync(
        Guid jobId,
        DateTimeOffset? lockedAtUtc,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.EmailProcessingJobs
            .Where(candidate => candidate.Id == jobId);

        if (lockedAtUtc.HasValue)
        {
            query = query.Where(
                candidate =>
                    candidate.Status == EmailProcessingStatus.Processing &&
                    candidate.LockedAtUtc == lockedAtUtc);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private static string FormatFailure(string error)
    {
        var normalized = string.IsNullOrWhiteSpace(error)
            ? "Unknown email analysis failure."
            : error.Trim();

        return normalized.Contains(EmailAnalysisVersion.Marker, StringComparison.Ordinal)
            ? normalized
            : $"{EmailAnalysisVersion.Marker} {normalized}";
    }
}
