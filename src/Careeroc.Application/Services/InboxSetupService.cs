using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Inbox;
using Careeroc.Domain.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class InboxSetupService
{
    private const int DefaultBackfillMonths = 3;
    private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(15);

    private readonly ICareerocDbContext _dbContext;
    private readonly IGmailInboxClient _gmailInboxClient;
    private readonly IInboxAccountTokenProtector _tokenProtector;
    private readonly IClock _clock;

    public InboxSetupService(
        ICareerocDbContext dbContext,
        IGmailInboxClient gmailInboxClient,
        IClock clock)
        : this(
            dbContext,
            gmailInboxClient,
            NoopInboxAccountTokenProtector.Instance,
            clock)
    {
    }

    public InboxSetupService(
        ICareerocDbContext dbContext,
        IGmailInboxClient gmailInboxClient,
        IInboxAccountTokenProtector tokenProtector,
        IClock clock)
    {
        _dbContext = dbContext;
        _gmailInboxClient = gmailInboxClient;
        _tokenProtector = tokenProtector;
        _clock = clock;
    }

    public async Task<InboxSetupResponse> GetSetupAsync(
        string? userEmail = null,
        bool gmailOnly = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.InboxAccounts)
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        var visibleAccounts = user?.InboxAccounts
            .Where(
                account =>
                    !gmailOnly ||
                    account.Provider == ApplicationDefaults.GmailInboxProvider ||
                    account.Provider == ApplicationDefaults.DemoInboxProvider)
            .ToList() ?? [];

        var accounts = visibleAccounts
            .OrderByDescending(account => account.CreatedAtUtc)
            .Select(MapAccount)
            .ToList();

        return new InboxSetupResponse(accounts);
    }

    public async Task<InboxSetupResponse> RegisterGmailAsync(
        RegisterInboxAccountRequest request,
        string? userEmail = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var emailAddress = NormalizeEmailAddress(request.EmailAddress);
        ValidateGmailAddress(emailAddress);

        var user = await GetOrCreateUserAsync(userEmail, userName, cancellationToken);
        var account = await UpsertAccountAsync(
            user,
            ApplicationDefaults.GmailInboxProvider,
            emailAddress,
            CreateDefaultMetadata(ApplicationDefaults.GmailInboxProvider, emailAddress),
            cancellationToken);

        var metadata = ReadMetadata(account);
        var (backfillMonths, backfillAllHistory) = ResolveRequestedBackfillSettings(
            request.BackfillMonths,
            metadata.BackfillMonths,
            metadata.BackfillAllHistory);
        metadata.SetupMode = "oauth";
        metadata.BackfillMonths = backfillMonths;
        metadata.BackfillAllHistory = backfillAllHistory;
        metadata.InitialBackfillCompletedAtUtc = null;
        metadata.SentReplyBackfillCompletedAtUtc = null;
        metadata.ConnectionStatus = _gmailInboxClient.IsConfigured ? "Ready for OAuth" : "OAuth config missing";
        metadata.NextStep = _gmailInboxClient.IsConfigured
            ? $"Use Continue with Google to grant Gmail read access and backfill {FormatBackfillWindow(backfillMonths, backfillAllHistory)} of recruiting email."
            : "Set Google__ClientId and Google__ClientSecret, then retry the Gmail connection flow.";

        WriteMetadata(account, metadata);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetSetupAsync(user.Email, gmailOnly: true, cancellationToken: cancellationToken);
    }

    public async Task<string> StartGmailOAuthAsync(
        RegisterInboxAccountRequest request,
        string redirectUri,
        string? returnUrl,
        string? userEmail = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        if (!_gmailInboxClient.IsConfigured)
        {
            throw new InvalidOperationException("Google OAuth is not configured. Set Google__ClientId and Google__ClientSecret.");
        }

        var emailAddress = NormalizeEmailAddress(request.EmailAddress);
        ValidateGmailAddress(emailAddress);

        var user = await GetOrCreateUserAsync(userEmail, userName, cancellationToken);
        var account = await UpsertAccountAsync(
            user,
            ApplicationDefaults.GmailInboxProvider,
            emailAddress,
            CreateDefaultMetadata(ApplicationDefaults.GmailInboxProvider, emailAddress),
            cancellationToken);

        var metadata = ReadMetadata(account);
        var (backfillMonths, backfillAllHistory) = ResolveRequestedBackfillSettings(
            request.BackfillMonths,
            metadata.BackfillMonths,
            metadata.BackfillAllHistory);
        metadata.SetupMode = "oauth";
        metadata.BackfillMonths = backfillMonths;
        metadata.BackfillAllHistory = backfillAllHistory;
        metadata.InitialBackfillCompletedAtUtc = null;
        metadata.SentReplyBackfillCompletedAtUtc = null;
        metadata.ConnectionStatus = "Waiting for Google";
        metadata.NextStep = $"Finish the Google consent screen to connect this Gmail inbox and backfill {FormatBackfillWindow(backfillMonths, backfillAllHistory)} of recruiting email.";
        metadata.OAuthState = CreateSecureToken();
        metadata.OAuthCodeVerifier = CreateSecureToken();
        metadata.OAuthReturnUrl = NormalizeReturnUrl(returnUrl);
        metadata.OAuthStateExpiresAtUtc = _clock.UtcNow.Add(OAuthStateLifetime);

        WriteMetadata(account, metadata);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var codeChallenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.UTF8.GetBytes(metadata.OAuthCodeVerifier)));

        return _gmailInboxClient.BuildAuthorizationUrl(
            metadata.OAuthState,
            codeChallenge,
            redirectUri,
            emailAddress);
    }

    public async Task<GmailOAuthCompletionResult> CompleteGmailOAuthAsync(
        string state,
        string code,
        string redirectUri,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Missing OAuth state or authorization code.");
        }

        var pendingAccount = await FindGmailAccountByStateAsync(state, userEmail, cancellationToken);
        if (pendingAccount is null)
        {
            throw new InvalidOperationException("No pending Gmail authorization session was found.");
        }

        var pendingMetadata = ReadMetadata(pendingAccount);
        var returnUrl = pendingMetadata.OAuthReturnUrl ?? "/";
        if (IsOAuthStateExpired(pendingMetadata))
        {
            ClearPendingOAuth(pendingMetadata);
            pendingMetadata.ConnectionStatus = "OAuth session expired";
            pendingMetadata.NextStep = "Retry Continue with Google to start a fresh Gmail authorization session.";
            WriteMetadata(pendingAccount, pendingMetadata);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("The Gmail authorization session expired. Start the flow again.");
        }

        if (string.IsNullOrWhiteSpace(pendingMetadata.OAuthCodeVerifier))
        {
            throw new InvalidOperationException("The Gmail authorization session is incomplete. Start the flow again.");
        }

        var authorization = await _gmailInboxClient.ExchangeCodeAsync(
            code,
            pendingMetadata.OAuthCodeVerifier,
            redirectUri,
            cancellationToken);

        var profile = await _gmailInboxClient.GetProfileAsync(authorization.AccessToken, cancellationToken);
        var actualEmailAddress = NormalizeEmailAddress(profile.EmailAddress);
        ValidateGmailAddress(actualEmailAddress);

        var targetAccount = await ResolveGmailAccountAsync(pendingAccount, actualEmailAddress, cancellationToken);
        var targetMetadata = ReadMetadata(targetAccount);

        targetAccount.Provider = ApplicationDefaults.GmailInboxProvider;
        targetAccount.EmailAddress = actualEmailAddress;

        targetMetadata.SetupMode = "oauth";
        targetMetadata.ConnectionStatus = "Connected";
        targetMetadata.NextStep = "Gmail is connected. The sync worker can now pull recent emails from the Gmail API.";
        targetMetadata.AccessToken = _tokenProtector.Protect(authorization.AccessToken);
        targetMetadata.RefreshToken = _tokenProtector.Protect(
            authorization.RefreshToken ?? targetMetadata.RefreshToken);
        targetMetadata.AccessTokenExpiresAtUtc = authorization.ExpiresAtUtc;
        targetMetadata.GmailHistoryId = profile.HistoryId;
        targetMetadata.ConnectedAtUtc = _clock.UtcNow;
        targetMetadata.BackfillMonths = pendingMetadata.BackfillMonths;
        targetMetadata.BackfillAllHistory = pendingMetadata.BackfillAllHistory;
        targetMetadata.InitialBackfillCompletedAtUtc = null;
        targetMetadata.SentReplyBackfillCompletedAtUtc = null;
        targetMetadata.OAuthState = null;
        targetMetadata.OAuthCodeVerifier = null;
        targetMetadata.OAuthReturnUrl = null;
        targetMetadata.OAuthStateExpiresAtUtc = null;

        WriteMetadata(targetAccount, targetMetadata);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new GmailOAuthCompletionResult(
            returnUrl,
            true,
            $"Connected Gmail inbox {actualEmailAddress}.");
    }

    public async Task<GmailOAuthCompletionResult> FailGmailOAuthAsync(
        string state,
        string? error,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var pendingAccount = await FindGmailAccountByStateAsync(state, userEmail, cancellationToken);
        if (pendingAccount is null)
        {
            return new GmailOAuthCompletionResult("/", false, error ?? "Google OAuth did not complete.");
        }

        var metadata = ReadMetadata(pendingAccount);
        var returnUrl = metadata.OAuthReturnUrl ?? "/";

        metadata.ConnectionStatus = "OAuth not completed";
        metadata.NextStep = "Retry Continue with Google to grant Gmail read access.";
        metadata.OAuthState = null;
        metadata.OAuthCodeVerifier = null;
        metadata.OAuthReturnUrl = null;
        metadata.OAuthStateExpiresAtUtc = null;

        WriteMetadata(pendingAccount, metadata);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new GmailOAuthCompletionResult(
            returnUrl,
            false,
            error ?? "Google OAuth did not complete.");
    }

    public async Task<InboxSetupResponse?> UpdateGmailBackfillAsync(
        Guid inboxAccountId,
        UpdateInboxBackfillRequest request,
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var account = await _dbContext.InboxAccounts
            .FirstOrDefaultAsync(
                candidate => candidate.Id == inboxAccountId && candidate.User.Email == resolvedUserEmail,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        if (account.Provider != ApplicationDefaults.GmailInboxProvider)
        {
            throw new InvalidOperationException("Backfill settings can only be updated for Gmail inboxes.");
        }

        var metadata = ReadMetadata(account);
        var (backfillMonths, backfillAllHistory) = ResolveRequestedBackfillSettings(
            request.BackfillMonths,
            metadata.BackfillMonths,
            metadata.BackfillAllHistory);
        var changed =
            metadata.BackfillMonths != backfillMonths ||
            metadata.BackfillAllHistory != backfillAllHistory;

        metadata.BackfillMonths = backfillMonths;
        metadata.BackfillAllHistory = backfillAllHistory;

        if (changed)
        {
            metadata.GmailHistoryId = null;
            metadata.InitialBackfillCompletedAtUtc = null;
            metadata.SentReplyBackfillCompletedAtUtc = null;
        }

        if (metadata.ConnectionStatus == "Connected")
        {
            metadata.NextStep = changed
                ? $"Gmail is connected. careeroc will backfill {FormatBackfillWindow(backfillMonths, backfillAllHistory)} of recruiting email on the next sync."
                : metadata.NextStep;
        }
        else
        {
            metadata.NextStep = $"Finish the Google consent screen to connect this Gmail inbox and backfill {FormatBackfillWindow(backfillMonths, backfillAllHistory)} of recruiting email.";
        }

        WriteMetadata(account, metadata);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetSetupAsync(resolvedUserEmail, gmailOnly: true, cancellationToken: cancellationToken);
    }

    public async Task<InboxSetupResponse?> DeleteInboxAccountAsync(
        Guid inboxAccountId,
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var account = await _dbContext.InboxAccounts
            .FirstOrDefaultAsync(
                candidate => candidate.Id == inboxAccountId && candidate.User.Email == resolvedUserEmail,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        var metadata = ReadMetadata(account);
        var hasImportedEmail = await _dbContext.InboxEmails
            .AnyAsync(email => email.InboxAccountId == inboxAccountId, cancellationToken);

        if (!string.Equals(metadata.ConnectionStatus, "Connected", StringComparison.OrdinalIgnoreCase) &&
            !hasImportedEmail)
        {
            _dbContext.InboxAccounts.Remove(account);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return await GetSetupAsync(resolvedUserEmail, gmailOnly: true, cancellationToken: cancellationToken);
        }

        metadata.ConnectionStatus = "Reconnect required";
        metadata.NextStep = "Gmail access was disconnected. Reconnect Google to resume syncing this inbox.";
        metadata.OAuthState = null;
        metadata.OAuthCodeVerifier = null;
        metadata.OAuthReturnUrl = null;
        metadata.OAuthStateExpiresAtUtc = null;
        metadata.AccessToken = null;
        metadata.RefreshToken = null;
        metadata.AccessTokenExpiresAtUtc = null;
        metadata.GmailHistoryId = null;
        WriteMetadata(account, metadata);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetSetupAsync(resolvedUserEmail, gmailOnly: true, cancellationToken: cancellationToken);
    }

    public async Task<DeleteImportedInboxDataResponse?> DeleteImportedDataAsync(
        Guid inboxAccountId,
        DeleteImportedInboxDataRequest request,
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var account = await _dbContext.InboxAccounts
            .FirstOrDefaultAsync(
                candidate => candidate.Id == inboxAccountId && candidate.User.Email == resolvedUserEmail,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        if (!string.Equals(request.Confirmation?.Trim(), account.EmailAddress, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Type the inbox email address to delete imported data.");
        }

        var inboxEmails = await _dbContext.InboxEmails
            .Where(email => email.InboxAccountId == inboxAccountId)
            .ToListAsync(cancellationToken);
        var inboxEmailIds = inboxEmails.Select(email => email.Id).ToHashSet();
        var preservedApplications = inboxEmails
            .Where(email => email.JobApplicationId.HasValue)
            .Select(email => email.JobApplicationId!.Value)
            .Distinct()
            .Count();
        var processingJobs = inboxEmailIds.Count == 0
            ? []
            : await _dbContext.EmailProcessingJobs
                .Where(job => inboxEmailIds.Contains(job.InboxEmailId))
                .ToListAsync(cancellationToken);
        var linkedEvents = inboxEmailIds.Count == 0
            ? []
            : await _dbContext.JobApplicationEvents
                .Where(@event => @event.InboxEmailId.HasValue && inboxEmailIds.Contains(@event.InboxEmailId.Value))
                .ToListAsync(cancellationToken);

        foreach (var linkedEvent in linkedEvents)
        {
            linkedEvent.InboxEmailId = null;
            linkedEvent.InboxEmail = null;
        }

        if (processingJobs.Count > 0)
        {
            _dbContext.EmailProcessingJobs.RemoveRange(processingJobs);
        }

        if (inboxEmails.Count > 0)
        {
            _dbContext.InboxEmails.RemoveRange(inboxEmails);
        }

        var metadata = ReadMetadata(account);
        metadata.ConnectionStatus = "Imported data deleted";
        metadata.NextStep = "Imported Gmail data was deleted and syncing is paused. Reconnect Google to import this inbox again.";
        metadata.OAuthState = null;
        metadata.OAuthCodeVerifier = null;
        metadata.OAuthReturnUrl = null;
        metadata.OAuthStateExpiresAtUtc = null;
        metadata.AccessToken = null;
        metadata.RefreshToken = null;
        metadata.AccessTokenExpiresAtUtc = null;
        metadata.GmailHistoryId = null;
        metadata.InitialBackfillCompletedAtUtc = null;
        metadata.SentReplyBackfillCompletedAtUtc = null;
        WriteMetadata(account, metadata);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteImportedInboxDataResponse(
            account.Id,
            account.EmailAddress,
            inboxEmails.Count,
            processingJobs.Count,
            linkedEvents.Count,
            preservedApplications);
    }

    public async Task<DeleteInboxPurgeResponse?> PurgeInboxAndImportedApplicationsAsync(
        Guid inboxAccountId,
        DeleteInboxPurgeRequest request,
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var account = await _dbContext.InboxAccounts
            .FirstOrDefaultAsync(
                candidate => candidate.Id == inboxAccountId && candidate.User.Email == resolvedUserEmail,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        var requiredConfirmation = $"DELETE {account.EmailAddress}";
        if (!string.Equals(request.Confirmation?.Trim(), requiredConfirmation, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Type {requiredConfirmation} to purge this inbox and its imported applications.");
        }

        var inboxEmails = await _dbContext.InboxEmails
            .Where(email => email.InboxAccountId == inboxAccountId)
            .ToListAsync(cancellationToken);
        var inboxEmailIds = inboxEmails.Select(email => email.Id).ToHashSet();
        var candidateApplicationIds = inboxEmails
            .Where(email => email.JobApplicationId.HasValue)
            .Select(email => email.JobApplicationId!.Value)
            .Distinct()
            .ToHashSet();
        var applicationIdsWithOtherInboxEvidence = candidateApplicationIds.Count == 0
            ? []
            : await _dbContext.InboxEmails
                .Where(
                    email =>
                        email.JobApplicationId.HasValue &&
                        candidateApplicationIds.Contains(email.JobApplicationId.Value) &&
                        email.InboxAccountId != inboxAccountId)
                .Select(email => email.JobApplicationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        var applicationIdsWithOtherInboxEvidenceSet = applicationIdsWithOtherInboxEvidence.ToHashSet();
        var purgeApplicationIds = candidateApplicationIds
            .Where(applicationId => !applicationIdsWithOtherInboxEvidenceSet.Contains(applicationId))
            .ToHashSet();
        var applications = purgeApplicationIds.Count == 0
            ? []
            : await _dbContext.JobApplications
                .Where(
                    application =>
                        application.UserId == account.UserId &&
                        application.Source == "Email" &&
                        purgeApplicationIds.Contains(application.Id))
                .ToListAsync(cancellationToken);
        var applicationIdsToDelete = applications
            .Select(application => application.Id)
            .ToHashSet();
        var processingJobs = inboxEmailIds.Count == 0
            ? []
            : await _dbContext.EmailProcessingJobs
                .Where(job => inboxEmailIds.Contains(job.InboxEmailId))
                .ToListAsync(cancellationToken);
        var eventsToDelete = applicationIdsToDelete.Count == 0
            ? []
            : await _dbContext.JobApplicationEvents
                .Where(@event => applicationIdsToDelete.Contains(@event.JobApplicationId))
                .ToListAsync(cancellationToken);
        var eventsToUnlink = inboxEmailIds.Count == 0
            ? []
            : await _dbContext.JobApplicationEvents
                .Where(
                    @event =>
                        @event.InboxEmailId.HasValue &&
                        inboxEmailIds.Contains(@event.InboxEmailId.Value) &&
                        !applicationIdsToDelete.Contains(@event.JobApplicationId))
                .ToListAsync(cancellationToken);
        var reminders = applicationIdsToDelete.Count == 0
            ? []
            : await _dbContext.Reminders
                .Where(reminder => applicationIdsToDelete.Contains(reminder.JobApplicationId))
                .ToListAsync(cancellationToken);

        foreach (var linkedEvent in eventsToUnlink)
        {
            linkedEvent.InboxEmailId = null;
            linkedEvent.InboxEmail = null;
        }

        if (processingJobs.Count > 0)
        {
            _dbContext.EmailProcessingJobs.RemoveRange(processingJobs);
        }

        if (eventsToDelete.Count > 0)
        {
            _dbContext.JobApplicationEvents.RemoveRange(eventsToDelete);
        }

        if (reminders.Count > 0)
        {
            _dbContext.Reminders.RemoveRange(reminders);
        }

        if (inboxEmails.Count > 0)
        {
            _dbContext.InboxEmails.RemoveRange(inboxEmails);
        }

        if (applications.Count > 0)
        {
            _dbContext.JobApplications.RemoveRange(applications);
        }

        _dbContext.InboxAccounts.Remove(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteInboxPurgeResponse(
            account.Id,
            account.EmailAddress,
            inboxEmails.Count,
            processingJobs.Count,
            applications.Count,
            reminders.Count,
            eventsToDelete.Count,
            eventsToUnlink.Count,
            1);
    }

    private async Task<User> GetOrCreateUserAsync(
        string? userEmail,
        string? userName,
        CancellationToken cancellationToken)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var resolvedUserName = ResolveUserName(userName, resolvedUserEmail);
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        if (user is not null)
        {
            return user;
        }

        user = new User
        {
            Email = resolvedUserEmail,
            Name = resolvedUserName
        };

        await _dbContext.Users.AddAsync(user, cancellationToken);
        return user;
    }

    private async Task<InboxAccount> UpsertAccountAsync(
        User user,
        string provider,
        string emailAddress,
        InboxAccountMetadata defaultMetadata,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.InboxAccounts
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.UserId == user.Id &&
                    candidate.EmailAddress == emailAddress,
                cancellationToken);

        if (account is null)
        {
            account = new InboxAccount
            {
                UserId = user.Id,
                User = user,
                Provider = provider,
                EmailAddress = emailAddress
            };

            await _dbContext.InboxAccounts.AddAsync(account, cancellationToken);
        }

        account.Provider = provider;
        account.EmailAddress = emailAddress;

        if (string.IsNullOrWhiteSpace(account.OAuthMetadataJson))
        {
            WriteMetadata(account, defaultMetadata);
        }

        return account;
    }

    private InboxAccountSetupDto MapAccount(InboxAccount account)
    {
        var metadata = ReadMetadata(account);

        return new InboxAccountSetupDto(
            account.Id,
            account.Provider,
            ToProviderLabel(account.Provider),
            account.EmailAddress,
            metadata.SetupMode,
            metadata.ConnectionStatus,
            metadata.NextStep,
            account.CreatedAtUtc,
            account.LastSyncedAtUtc,
            metadata.BackfillMonths,
            metadata.BackfillAllHistory,
            metadata.InitialBackfillCompletedAtUtc,
            metadata.IsValid,
            metadata.InvalidReason);
    }

    private InboxAccountMetadata ReadMetadata(InboxAccount account)
    {
        var metadata = InboxAccountMetadataJson.Read(
            account,
            candidate => CreateDefaultMetadata(candidate.Provider, candidate.EmailAddress));

        if (account.Provider == ApplicationDefaults.GmailInboxProvider &&
            !metadata.BackfillAllHistory &&
            !metadata.BackfillMonths.HasValue)
        {
            metadata.BackfillMonths = DefaultBackfillMonths;
        }

        return metadata;
    }

    private static void WriteMetadata(InboxAccount account, InboxAccountMetadata metadata)
    {
        InboxAccountMetadataJson.Write(account, metadata);
    }

    private InboxAccountMetadata CreateDefaultMetadata(string provider, string emailAddress)
    {
        return provider switch
        {
            ApplicationDefaults.GmailInboxProvider => new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = _gmailInboxClient.IsConfigured ? "Ready for OAuth" : "OAuth config missing",
                NextStep = _gmailInboxClient.IsConfigured
                    ? "Use Continue with Google to grant Gmail read access."
                    : "Set Google__ClientId and Google__ClientSecret, then retry the Gmail connection flow.",
                BackfillMonths = DefaultBackfillMonths
            },
            _ => new InboxAccountMetadata
            {
                SetupMode = "manual",
                ConnectionStatus = "Configured",
                NextStep = "Connection details are stored locally."
            }
        };
    }

    // Only the user who started the flow can finish it: otherwise a stranger who consents through a
    // forwarded Google link would attach their mailbox to the account that created the link.
    private async Task<InboxAccount?> FindGmailAccountByStateAsync(
        string state,
        string? userEmail,
        CancellationToken cancellationToken)
    {
        var normalizedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var gmailAccounts = await _dbContext.InboxAccounts
            .Where(account =>
                account.Provider == ApplicationDefaults.GmailInboxProvider &&
                account.User.Email == normalizedUserEmail)
            .ToListAsync(cancellationToken);

        return gmailAccounts.FirstOrDefault(
            account => string.Equals(ReadMetadata(account).OAuthState, state, StringComparison.Ordinal));
    }

    private async Task<InboxAccount> ResolveGmailAccountAsync(
        InboxAccount pendingAccount,
        string actualEmailAddress,
        CancellationToken cancellationToken)
    {
        if (string.Equals(pendingAccount.EmailAddress, actualEmailAddress, StringComparison.OrdinalIgnoreCase))
        {
            pendingAccount.EmailAddress = actualEmailAddress;
            return pendingAccount;
        }

        var existingAccount = await _dbContext.InboxAccounts
            .FirstOrDefaultAsync(
                account =>
                    account.Id != pendingAccount.Id &&
                    account.UserId == pendingAccount.UserId &&
                    account.Provider == ApplicationDefaults.GmailInboxProvider &&
                    account.EmailAddress == actualEmailAddress,
                cancellationToken);

        if (existingAccount is null)
        {
            pendingAccount.EmailAddress = actualEmailAddress;
            return pendingAccount;
        }

        _dbContext.InboxAccounts.Remove(pendingAccount);
        return existingAccount;
    }

    private static string CreateSecureToken()
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private bool IsOAuthStateExpired(InboxAccountMetadata metadata)
    {
        return !metadata.OAuthStateExpiresAtUtc.HasValue ||
               metadata.OAuthStateExpiresAtUtc.Value <= _clock.UtcNow;
    }

    private static void ClearPendingOAuth(InboxAccountMetadata metadata)
    {
        metadata.OAuthState = null;
        metadata.OAuthCodeVerifier = null;
        metadata.OAuthReturnUrl = null;
        metadata.OAuthStateExpiresAtUtc = null;
    }

    private static string NormalizeEmailAddress(string emailAddress)
    {
        var trimmed = emailAddress.Trim().ToLowerInvariant();

        if (!MailAddress.TryCreate(trimmed, out var parsed) || !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Enter a valid email address.");
        }

        return trimmed;
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        return ApplicationDefaults.IsLocalPath(returnUrl) ? returnUrl : "/";
    }

    private static string GetDomain(string emailAddress)
    {
        var separatorIndex = emailAddress.LastIndexOf('@');
        if (separatorIndex < 0 || separatorIndex == emailAddress.Length - 1)
        {
            throw new InvalidOperationException("Enter a valid email address.");
        }

        return emailAddress[(separatorIndex + 1)..];
    }

    private static void ValidateGmailAddress(string emailAddress)
    {
        // Google Workspace mailboxes can also use the Gmail API flow.
        _ = GetDomain(emailAddress);
    }

    private static int? NormalizeBackfillMonths(int? backfillMonths)
    {
        if (backfillMonths == 0)
        {
            return null;
        }

        if (backfillMonths is null)
        {
            return null;
        }

        if (backfillMonths < 1 || backfillMonths > 24)
        {
            throw new InvalidOperationException("Choose a backfill window between 1 and 24 months, or use all history.");
        }

        return backfillMonths;
    }

    private static (int? BackfillMonths, bool BackfillAllHistory) ResolveRequestedBackfillSettings(
        int? requestedBackfillMonths,
        int? currentBackfillMonths,
        bool currentBackfillAllHistory)
    {
        if (requestedBackfillMonths.HasValue)
        {
            return requestedBackfillMonths.Value == 0
                ? (null, true)
                : (NormalizeBackfillMonths(requestedBackfillMonths), false);
        }

        if (currentBackfillAllHistory)
        {
            return (null, true);
        }

        return (currentBackfillMonths ?? DefaultBackfillMonths, false);
    }

    private static string FormatBackfillWindow(int? backfillMonths, bool backfillAllHistory)
    {
        return backfillAllHistory
            ? "all available history"
            : backfillMonths.HasValue
            ? $"the last {backfillMonths.Value} month{(backfillMonths.Value == 1 ? string.Empty : "s")}"
            : $"the last {DefaultBackfillMonths} months";
    }

    private static string ToProviderLabel(string provider)
    {
        return provider switch
        {
            ApplicationDefaults.GmailInboxProvider => "Gmail",
            ApplicationDefaults.DemoInboxProvider => "Demo inbox",
            _ => provider
        };
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }

    private static string ResolveUserName(string? userName, string userEmail)
    {
        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName.Trim();
        }

        var separatorIndex = userEmail.IndexOf('@');
        return separatorIndex > 0 ? userEmail[..separatorIndex] : ApplicationDefaults.DefaultUserName;
    }
}
