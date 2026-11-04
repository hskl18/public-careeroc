using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Careeroc.Api.Endpoints;
using Careeroc.Api.Security;
using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Admin;
using Careeroc.Contracts.Agents;
using Careeroc.Domain;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Careeroc.Tests;

public sealed class ApiRoutingSecurityTests
{
    private const string ApiSecret = "test-api-auth-secret-with-enough-entropy";

    [Fact]
    public async Task ApiRequestsRequireSignedUserContext()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Anonymous rejections are logged, not stored, so they cannot fill the audit table.
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        Assert.Empty(dbContext.AuditEvents);
    }

    [Theory]
    [InlineData("short-secret")]
    [InlineData("careeroc-dev-api-auth-secret-change-me")]
    public void ProductionApiRejectsWeakSigningSecrets(string weakSecret)
    {
        using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Careeroc:ApiAuthSecret"] = weakSecret
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("API request signing secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeFallbackSecretsOnlyWorkWhileOnlyDemoVisitorsCanSignIn()
    {
        var composeSecrets = new Dictionary<string, string?>
        {
            ["Careeroc:ApiAuthSecret"] = "local-compose-only-api-signing-secret-0001",
            ["Security:TokenProtectionKey"] = "local-compose-only-token-protection-key-01",
            ["Demo:Enabled"] = "true",
            ["Access:AllowedDomains"] = "demo.careeroc.example"
        };
        using (var demo = new CareerocApiFactory(composeSecrets))
        {
            using var _ = demo.CreateClient();
        }

        using var realAccounts = new CareerocApiFactory(
            new Dictionary<string, string?>(composeSecrets) { ["Access:AllowedDomains"] = "demo.careeroc.example,example.com" });
        var exception = Assert.Throws<InvalidOperationException>(() => realAccounts.CreateClient());

        Assert.Contains("public fallback from docker-compose.yml", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short-for-a-shared-secret")]
    public void ProductionApiNeedsAnAgentRuntimeSecretWhenTheRuntimeIsEnabled(string? secret)
    {
        using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["AgentRuntime:Enabled"] = "true",
                ["AgentRuntime:SharedSecret"] = secret
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AgentRuntime:SharedSecret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionApiRejectsMissingAccessPolicy()
    {
        using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Access:AllowAllUsers"] = "false"
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("explicit production access policy", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short-token-key")]
    [InlineData("careeroc-dev-token-protection-key-change-me")]
    public void ProductionApiRejectsMissingOrWeakInboxTokenProtectionKey(string? tokenProtectionKey)
    {
        using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Security:TokenProtectionKey"] = tokenProtectionKey
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("inbox token protection key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignedApiRequestsReachEndpoint()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        Sign(request, userEmail: "student@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("student@example")]
    [InlineData("@example.com")]
    public async Task SignedApiRequestsRejectMalformedUserEmail(string malformedEmail)
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        Sign(request, userEmail: malformedEmail);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void ApiRouteSecurityCoverageListIsExplicit()
    {
        using var factory = new CareerocApiFactory();
        using var _ = factory.CreateClient();

        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var apiRoutes = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(
                endpoint =>
                {
                    var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
                    var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
                    return methods.Select(method => $"{method} {pattern}");
                })
            .Where(route => route.Contains(" /api/", StringComparison.Ordinal))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToList();

        var coveredRoutes = new HashSet<string>(StringComparer.Ordinal)
        {
            "DELETE /api/inbox-accounts/{inboxAccountId:guid}",
            "DELETE /api/inbox-accounts/{inboxAccountId:guid}/imported-data",
            "DELETE /api/inbox-accounts/{inboxAccountId:guid}/purge",
            "DELETE /api/profile/access-tokens/{tokenId:guid}",
            "DELETE /api/profile/memory/{memoryEntryId:guid}",
            "GET /api/admin/health",
            "GET /api/admin/summary",
            "GET /api/analytics",
            "GET /api/applications/lookup",
            "GET /api/applications/{jobApplicationId:guid}",
            "GET /api/applications/{jobApplicationId:guid}/resume-fit",
            "GET /api/dashboard",
            "GET /api/inbox-accounts",
            "GET /api/inbox-accounts/gmail/oauth/callback",
            "GET /api/inbox-accounts/gmail/oauth/start",
            "GET /api/profile",
            "GET /api/profile/access-tokens",
            "GET /api/profile/agent-context",
            "GET /api/profile/memory",
            "GET /api/profile/memory/proposals",
            "GET /api/recruiting-contacts",
            "GET /api/resumes",
            "GET /api/resumes/{resumeDocumentId:guid}/suggestions",
            "GET /api/review",
            "PATCH /api/applications/{jobApplicationId:guid}/artifacts/{artifactEvidenceId:guid}/review",
            "PATCH /api/applications/{jobApplicationId:guid}/correction",
            "PATCH /api/applications/{jobApplicationId:guid}/status",
            "PATCH /api/emails/{inboxEmailId:guid}/correction",
            "PATCH /api/inbox-accounts/{inboxAccountId:guid}/backfill",
            "PATCH /api/profile",
            "PATCH /api/profile/memory/{memoryEntryId:guid}",
            "PATCH /api/profile/memory/proposals/{memoryEntryId:guid}",
            "PATCH /api/recruiting-contacts/{contactId:guid}",
            "PATCH /api/reminders/{reminderId:guid}/snooze",
            "PATCH /api/reminders/{reminderId:guid}/status",
            "PATCH /api/resumes/versions/{resumeVersionId:guid}/structured-data",
            "POST /api/admin/email-processing-jobs/{jobId:guid}/retry",
            "POST /api/applications/{jobApplicationId:guid}/artifacts",
            "POST /api/dashboard/inbox-check",
            "POST /api/inbox-accounts/sync-now",
            "POST /api/inbox-accounts/gmail",
            "POST /api/orchestrator/resume-mailbox-review",
            "POST /api/profile/access-tokens",
            "POST /api/profile/welcome-dismissed",
            "POST /api/reminders/{reminderId:guid}/evidence-refresh",
            "POST /api/resumes/import",
            "POST /api/resumes",
            "POST /api/resumes/{resumeDocumentId:guid}/reextract",
            "POST /api/resumes/{resumeDocumentId:guid}/suggestions/{suggestionId:guid}/accept",
            "POST /api/resumes/{resumeDocumentId:guid}/suggestions/{suggestionId:guid}/decline",
            "POST /api/resumes/versions/{resumeVersionId:guid}/analyze",
            "POST /api/resumes/versions/{resumeVersionId:guid}/corrections"
        };

        Assert.Empty(apiRoutes.Except(coveredRoutes));
        Assert.Empty(coveredRoutes.Except(apiRoutes));
    }

    [Fact]
    public void AdminEndpointsUseSharedRequireAdminGuard()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "apps",
            "api",
            "Endpoints",
            "AdminEndpoints.cs"));

        Assert.DoesNotContain("RequestUserContext.IsAdmin(", source, StringComparison.Ordinal);
        Assert.Contains("AdminAuthorization.RequireAdmin(request)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GmailOAuthCallbackNeedsTheSignedUserAndRedirectsWithinTheSite()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        const string callback = "/api/inbox-accounts/gmail/oauth/callback?state=missing&code=missing";

        var unsigned = await client.GetAsync(callback);
        var signed = await SendSignedAsync(client, HttpMethod.Get, callback, "student@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, signed.StatusCode);
        Assert.False(signed.Headers.Location!.IsAbsoluteUri);
        Assert.StartsWith("/app/inbox?gmail_status=error", signed.Headers.Location.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentEndpointsAreAbsentOutsideDevelopment()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/development/bootstrap");
        request.Content = new StringContent("", Encoding.UTF8, "application/json");
        Sign(request, "", "student@example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task QueryEndpointsReturnOnlySignedUsersData()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();
        var seed = await factory.SeedTwoUsersAsync();

        var ownApplication = await SendSignedAsync(client, HttpMethod.Get, $"/api/applications/{seed.StudentApplicationId}", seed.StudentEmail);
        var otherApplication = await SendSignedAsync(client, HttpMethod.Get, $"/api/applications/{seed.OtherApplicationId}", seed.StudentEmail);
        var dashboard = await SendSignedAsync(client, HttpMethod.Get, "/api/dashboard", seed.StudentEmail);
        var review = await SendSignedAsync(client, HttpMethod.Get, "/api/review", seed.StudentEmail);
        var analytics = await SendSignedAsync(client, HttpMethod.Get, "/api/analytics", seed.StudentEmail);
        var inboxAccounts = await SendSignedAsync(client, HttpMethod.Get, "/api/inbox-accounts", seed.StudentEmail);
        var profile = await SendSignedAsync(client, HttpMethod.Get, "/api/profile", seed.StudentEmail);

        Assert.Equal(HttpStatusCode.OK, ownApplication.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherApplication.StatusCode);

        await AssertContainsOnlyAsync(dashboard, "Student Visible Co", "Other Hidden Co", "other@example.com");
        await AssertContainsOnlyAsync(review, "Student Visible Co", "Other Hidden Co", "other@example.com");
        await AssertContainsOnlyAsync(analytics, "Student Visible Co", "Other Hidden Co", "other@example.com");
        await AssertContainsOnlyAsync(inboxAccounts, "student.visible@gmail.com", "other@example.com", "Other Hidden Co");
        await AssertContainsOnlyAsync(profile, "student@example.com", "other@example.com", "Other Hidden Co");
    }

    [Fact]
    public async Task ProfileMemoryManagementEndpointsAreUserScopedAndAudited()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();
        await factory.SeedTwoUsersAsync();

        Guid studentMemoryId;
        Guid otherMemoryId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
            var student = await dbContext.Users.SingleAsync(user => user.Email == "student@example.com");
            var other = await dbContext.Users.SingleAsync(user => user.Email == "other@example.com");
            var studentMemory = new UserMemoryEntry
            {
                User = student,
                UserId = student.Id,
                Kind = "preference",
                Key = "target_stack",
                Value = "backend C#",
                SourceType = "user",
                ConfirmationStatus = "confirmed"
            };
            var otherMemory = new UserMemoryEntry
            {
                User = other,
                UserId = other.Id,
                Kind = "preference",
                Key = "private_goal",
                Value = "Other private memory",
                SourceType = "user",
                ConfirmationStatus = "confirmed"
            };
            dbContext.UserMemoryEntries.AddRange(studentMemory, otherMemory);
            await dbContext.SaveChangesAsync();
            studentMemoryId = studentMemory.Id;
            otherMemoryId = otherMemory.Id;
        }

        var listed = await SendSignedAsync(client, HttpMethod.Get, "/api/profile/memory", "student@example.com");
        var crossUserUpdate = await SendSignedJsonAsync(
            client,
            $"/api/profile/memory/{otherMemoryId}",
            new { value = "stolen", summary = (string?)null, confidence = 1.0, expiresAtUtc = (DateTimeOffset?)null },
            "student@example.com",
            HttpMethod.Patch);
        var ownUpdate = await SendSignedJsonAsync(
            client,
            $"/api/profile/memory/{studentMemoryId}",
            new { value = "backend C# and TypeScript", summary = "User corrected preference.", confidence = 0.95, expiresAtUtc = (DateTimeOffset?)null },
            "student@example.com",
            HttpMethod.Patch);
        var crossUserDelete = await SendSignedAsync(client, HttpMethod.Delete, $"/api/profile/memory/{otherMemoryId}", "student@example.com");
        var ownDelete = await SendSignedAsync(client, HttpMethod.Delete, $"/api/profile/memory/{studentMemoryId}", "student@example.com");

        await AssertContainsOnlyAsync(listed, "backend C#", "Other private memory", "other@example.com");
        Assert.Equal(HttpStatusCode.NotFound, crossUserUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossUserDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        Assert.DoesNotContain(verifyDbContext.UserMemoryEntries, entry => entry.Id == studentMemoryId);
        Assert.Contains(verifyDbContext.UserMemoryEntries, entry => entry.Id == otherMemoryId);
        var auditEvents = await verifyDbContext.AuditEvents.ToListAsync();
        Assert.Contains(
            auditEvents,
            auditEvent => auditEvent.EventType == "profile.memory_updated" &&
                          auditEvent.Outcome == "success" &&
                          auditEvent.ResourceId == studentMemoryId);
        Assert.Contains(
            auditEvents,
            auditEvent => auditEvent.EventType == "profile.memory_updated" &&
                          auditEvent.Outcome == "not_found" &&
                          auditEvent.ResourceId == otherMemoryId);
        Assert.Contains(
            auditEvents,
            auditEvent => auditEvent.EventType == "profile.memory_deleted" &&
                          auditEvent.Outcome == "success" &&
                          auditEvent.ResourceId == studentMemoryId);
    }

    [Fact]
    public async Task InboxPurgeEndpointRequiresConfirmationAndAuditsOutcome()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();
        var seed = await factory.SeedTwoUsersAsync();

        Guid inboxAccountId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
            inboxAccountId = await dbContext.InboxAccounts
                .Where(account => account.User.Email == seed.StudentEmail)
                .Select(account => account.Id)
                .SingleAsync();
        }

        var rejected = await SendSignedJsonAsync(
            client,
            $"/api/inbox-accounts/{inboxAccountId}/purge",
            new { confirmation = "student.visible@gmail.com" },
            seed.StudentEmail,
            HttpMethod.Delete);
        var purged = await SendSignedJsonAsync(
            client,
            $"/api/inbox-accounts/{inboxAccountId}/purge",
            new { confirmation = "DELETE student.visible@gmail.com" },
            seed.StudentEmail,
            HttpMethod.Delete);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, purged.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        var auditEvents = await verifyDbContext.AuditEvents.ToListAsync();

        Assert.DoesNotContain(verifyDbContext.InboxAccounts, account => account.Id == inboxAccountId);
        Assert.DoesNotContain(verifyDbContext.JobApplications, application => application.Id == seed.StudentApplicationId);
        Assert.Contains(verifyDbContext.JobApplications, application => application.Id == seed.OtherApplicationId);
        Assert.Contains(
            auditEvents,
            auditEvent => auditEvent.EventType == "inbox.account_purged" &&
                          auditEvent.Outcome == "rejected" &&
                          auditEvent.ResourceId == inboxAccountId);
        Assert.Contains(
            auditEvents,
            auditEvent => auditEvent.EventType == "inbox.account_purged" &&
                          auditEvent.Outcome == "success" &&
                          auditEvent.ResourceId == inboxAccountId);
    }

    [Fact]
    public async Task ConfiguredAllowlistRejectsSignedUsersOutsideAllowedEmails()
    {
        await using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Access:AllowAllUsers"] = "false",
                ["Access:AllowedEmails"] = "student@example.com"
            });
        using var client = factory.CreateClient();

        var allowed = await SendSignedAsync(client, HttpMethod.Get, "/api/dashboard", "student@example.com");
        var rejected = await SendSignedAsync(client, HttpMethod.Get, "/api/dashboard", "outside@example.com");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task ConfiguredAllowlistAllowsMatchingDomains()
    {
        await using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Access:AllowAllUsers"] = "false",
                ["Access:AllowedDomains"] = "example.edu"
            });
        using var client = factory.CreateClient();

        var allowed = await SendSignedAsync(client, HttpMethod.Get, "/api/dashboard", "student@example.edu");
        var rejected = await SendSignedAsync(client, HttpMethod.Get, "/api/dashboard", "student@example.com");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task AdminEndpointRequiresAdminRoleAndSeedsConfiguredAdmins()
    {
        await using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Admin:Emails"] = "admin@example.com"
            });
        using var client = factory.CreateClient();
        await factory.SeedTwoUsersAsync();
        using (var seedScope = factory.Services.CreateScope())
        {
            var seedDbContext = seedScope.ServiceProvider.GetRequiredService<CareerocDbContext>();
            var inboxAccount = await seedDbContext.InboxAccounts
                .SingleAsync(account => account.EmailAddress == "student.visible@gmail.com");
            inboxAccount.OAuthMetadataJson = """
                {"connectionStatus":"OAuth pending","refreshToken":"raw-refresh-token-secret"}
                """;
            await seedDbContext.SaveChangesAsync();
        }

        var nonAdmin = await SendSignedAsync(client, HttpMethod.Get, "/api/admin/health", "student@example.com");
        var admin = await SendSignedAsync(client, HttpMethod.Get, "/api/admin/health", "admin@example.com");
        var adminSummary = await SendSignedAsync(client, HttpMethod.Get, "/api/admin/summary", "admin@example.com");
        var nonAdminSummary = await SendSignedAsync(client, HttpMethod.Get, "/api/admin/summary", "student@example.com");
        var adminSummaryBody = await adminSummary.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, nonAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminSummary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminSummary.StatusCode);
        Assert.DoesNotContain(
            "raw-refresh-token-secret",
            adminSummaryBody,
            StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        var adminUser = await dbContext.Users.SingleAsync(user => user.Email == "admin@example.com");
        Assert.Equal(UserRoleNames.Admin, adminUser.Role);
        Assert.Contains(
            await dbContext.AuditEvents.ToListAsync(),
            auditEvent => auditEvent.EventType == "admin.summary_viewed" &&
                          auditEvent.UserEmail == "admin@example.com" &&
                          auditEvent.Outcome == "success");
    }

    [Fact]
    public async Task AdminJobRetryIsConfirmedScopedAndAudited()
    {
        await using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Admin:Emails"] = "admin@example.com"
            });
        using var client = factory.CreateClient();
        await factory.SeedTwoUsersAsync();

        Guid failedJobId;
        Guid inboxAccountId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
            var inboxEmail = await dbContext.InboxEmails
                .Include(email => email.InboxAccount)
                .FirstAsync(email => email.InboxAccount.User.Email == "student@example.com");
            inboxAccountId = inboxEmail.InboxAccountId;
            inboxEmail.InboxAccount.OAuthMetadataJson = JsonSerializer.Serialize(
                new InboxAccountMetadata
                {
                    SetupMode = "oauth",
                    ConnectionStatus = "OAuth pending",
                    NextStep = "Finish OAuth.",
                    OAuthState = "pending-state",
                    OAuthCodeVerifier = "pending-verifier",
                    OAuthReturnUrl = "/app/inbox",
                    OAuthStateExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var failedJob = new EmailProcessingJob
            {
                InboxEmailId = inboxEmail.Id,
                Status = EmailProcessingStatus.Failed,
                Attempts = 3,
                LastError = "Failure for recruiter@example.com with access_token=secret-token-value",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.EmailProcessingJobs.Add(failedJob);
            await dbContext.SaveChangesAsync();
            failedJobId = failedJob.Id;
        }

        var rejectedRetry = await SendSignedJsonAsync(
            client,
            $"/api/admin/email-processing-jobs/{failedJobId}/retry",
            new { confirmation = "wrong" },
            "admin@example.com");
        var nonAdminRetry = await SendSignedJsonAsync(
            client,
            $"/api/admin/email-processing-jobs/{failedJobId}/retry",
            new { confirmation = "retry" },
            "student@example.com");
        var retry = await SendSignedJsonAsync(
            client,
            $"/api/admin/email-processing-jobs/{failedJobId}/retry",
            new { confirmation = "retry" },
            "admin@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, rejectedRetry.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminRetry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        var retriedJob = await verifyDbContext.EmailProcessingJobs.SingleAsync(job => job.Id == failedJobId);
        var auditEvents = await verifyDbContext.AuditEvents.ToListAsync();

        Assert.Equal(EmailProcessingStatus.Pending, retriedJob.Status);
        Assert.Null(retriedJob.LastError);
        Assert.Equal(0, retriedJob.Attempts);
        Assert.Contains(
            auditEvents,
            auditEvent => auditEvent.EventType == "admin.email_job_retry" &&
                          auditEvent.Outcome == "success" &&
                          auditEvent.ResourceId == failedJobId);
    }

    [Fact]
    public async Task DemoWorkspaceEndpointIsAbsentUnlessDemoIsEnabled()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();

        var response = await SendSignedAsync(client, HttpMethod.Post, "/api/demo/workspace", DemoVisitorEmail);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DemoWorkspaceEndpointBuildsTheWorkspaceOnce()
    {
        await using var factory = new CareerocApiFactory(DemoConfiguration);
        using var client = factory.CreateClient();

        var first = await SendSignedAsync(client, HttpMethod.Post, "/api/demo/workspace", DemoVisitorEmail);
        var second = await SendSignedAsync(client, HttpMethod.Post, "/api/demo/workspace", DemoVisitorEmail);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.True(firstBody.RootElement.GetProperty("created").GetBoolean());
        Assert.True(firstBody.RootElement.GetProperty("applicationsCreated").GetInt32() >= 15);
        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.False(secondBody.RootElement.GetProperty("created").GetBoolean());
    }

    [Fact]
    public async Task DemoWorkspaceEndpointRejectsRealIdentities()
    {
        await using var factory = new CareerocApiFactory(DemoConfiguration);
        using var client = factory.CreateClient();

        var response = await SendSignedAsync(client, HttpMethod.Post, "/api/demo/workspace", "student@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DemoIdentitiesCannotConnectARealMailbox()
    {
        await using var factory = new CareerocApiFactory(DemoConfiguration);
        using var client = factory.CreateClient();

        var response = await SendSignedJsonAsync(
            client,
            "/api/inbox-accounts/gmail",
            new { emailAddress = "someone@gmail.com" },
            DemoVisitorEmail);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("demo_read_only", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentRoutesRejectRequestsWithoutAValidAccessToken()
    {
        await using var factory = new CareerocApiFactory();
        using var client = factory.CreateClient();
        using var withoutToken = new HttpRequestMessage(HttpMethod.Get, "/agent/v1/pipeline-summary");
        using var withBadToken = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        withBadToken.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "coc_not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(withoutToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(withBadToken)).StatusCode);
    }

    [Fact]
    public async Task AccessTokensUnlockOnlyTheirOwnersPipelineOverMcpAndRest()
    {
        await using var factory = new CareerocApiFactory();
        var seed = await factory.SeedTwoUsersAsync();
        using var client = factory.CreateClient();
        var createResponse = await SendSignedJsonAsync(
            client,
            "/api/profile/access-tokens",
            new { name = "Test assistant" },
            seed.StudentEmail);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var token = created.RootElement.GetProperty("token").GetString()!;

        using var summaryRequest = new HttpRequestMessage(HttpMethod.Get, "/agent/v1/pipeline-summary");
        summaryRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var summary = await client.SendAsync(summaryRequest);
        await AssertContainsOnlyAsync(summary, "Student Visible Co", "Other Hidden Co");

        var transport = new ModelContextProtocol.Client.HttpClientTransport(
            new ModelContextProtocol.Client.HttpClientTransportOptions
            {
                Endpoint = new Uri(client.BaseAddress!, "/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
            },
            client,
            ownsHttpClient: false);
        await using var mcp = await ModelContextProtocol.Client.McpClient.CreateAsync(transport);
        var tools = await mcp.ListToolsAsync();
        var result = await mcp.CallToolAsync("list_applications", new Dictionary<string, object?> { ["limit"] = 10 });
        var offers = await mcp.CallToolAsync("list_applications", new Dictionary<string, object?> { ["status"] = "offer" });

        Assert.Equal(["get_application", "get_pipeline_summary", "list_applications"], tools.Select(tool => tool.Name).Order().ToArray());
        var listSchema = tools.Single(tool => tool.Name == "list_applications").JsonSchema.GetRawText();
        Assert.Contains("\"assessment pending\"", listSchema, StringComparison.Ordinal);
        Assert.NotEqual(true, offers.IsError);
        Assert.DoesNotContain("Student Visible Co", string.Join(" ", offers.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text)), StringComparison.Ordinal);
        var text = string.Join(" ", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));
        Assert.Contains("Student Visible Co", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Hidden Co", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentCallerEndpointReturnsAStableOwnerAndRejectsBrowserOrigins()
    {
        await using var factory = new CareerocApiFactory();
        var seed = await factory.SeedTwoUsersAsync();
        using var client = factory.CreateClient();
        var createResponse = await SendSignedJsonAsync(client, "/api/profile/access-tokens", new { name = "Test assistant" }, seed.StudentEmail);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var token = created.RootElement.GetProperty("token").GetString()!;

        async Task<HttpResponseMessage> GetMeAsync(string? origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/agent/v1/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (origin is not null)
            {
                request.Headers.Add("Origin", origin);
            }

            return await client.SendAsync(request);
        }

        var first = await GetMeAsync(null);
        var second = await GetMeAsync(null);
        var fromBrowser = await GetMeAsync("https://attacker.example");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var owner = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("owner").GetString();
        Assert.Equal(64, owner?.Length);
        Assert.DoesNotContain("@", owner, StringComparison.Ordinal);
        Assert.Equal(owner, JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("owner").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, fromBrowser.StatusCode);
    }

    [Fact]
    public async Task AccessTokensStopWorkingWhenTheOwnerLeavesTheAllowlist()
    {
        await using var factory = new CareerocApiFactory(
            new Dictionary<string, string?>
            {
                ["Access:AllowedEmails"] = "someone-else@example.com"
            });
        var seed = await factory.SeedTwoUsersAsync();
        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>();
            token = (await tokens.CreateAsync(seed.StudentEmail, new CreatePersonalAccessTokenRequest("Test assistant", 1))).Token;
        }

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/agent/v1/pipeline-summary");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    private const string DemoVisitorEmail = "visitor-0001@demo.careeroc.example";

    private static readonly IReadOnlyDictionary<string, string?> DemoConfiguration = new Dictionary<string, string?>
    {
        ["Demo:Enabled"] = "true"
    };

    private static async Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string userEmail)
    {
        var request = new HttpRequestMessage(method, path);
        Sign(request, userEmail: userEmail);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendSignedJsonAsync(
        HttpClient client,
        string path,
        object body,
        string userEmail,
        HttpMethod? method = null)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        Sign(request, json, userEmail);
        return await client.SendAsync(request);
    }

    private static async Task AssertContainsOnlyAsync(
        HttpResponseMessage response,
        string expected,
        params string[] forbidden)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expected, body, StringComparison.OrdinalIgnoreCase);

        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void Sign(
        HttpRequestMessage request,
        string body = "",
        string userEmail = "student@example.com",
        string userName = "Student User")
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var email = userEmail;
        var name = userName;
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var pathAndQuery = request.RequestUri is null
            ? "/"
            : request.RequestUri.IsAbsoluteUri
                ? request.RequestUri.PathAndQuery
                : request.RequestUri.OriginalString;
        var payload = string.Join(
            '\n',
            request.Method.Method.ToUpperInvariant(),
            pathAndQuery,
            email,
            name,
            timestamp,
            bodyHash);
        var signature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(ApiSecret),
                    Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

        request.Headers.Add("X-Careeroc-User-Email", email);
        request.Headers.Add("X-Careeroc-User-Name", name);
        request.Headers.Add("X-Careeroc-Timestamp", timestamp);
        request.Headers.Add("X-Careeroc-Body-SHA256", bodyHash);
        request.Headers.Add("X-Careeroc-Signature", signature);
    }

    internal sealed class CareerocApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"careeroc-api-tests-{Guid.NewGuid():N}";
        private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

        public CareerocApiFactory(IReadOnlyDictionary<string, string?>? configurationOverrides = null)
        {
            _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration(
                configuration =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["Careeroc:ApiAuthSecret"] = ApiSecret,
                        // Satisfies the production database check; the test host swaps in an in-memory provider.
                        ["ConnectionStrings:Careeroc"] = "Host=unused;Database=unused",
                        ["SyncWorker:Enabled"] = "false",
                        ["Database:ApplyMigrationsOnStartup"] = "false",
                        ["Security:RateLimiting:AnonymousPermitLimit"] = "1000",
                        ["Security:RateLimiting:UserPermitLimit"] = "1000",
                        ["Security:RateLimiting:OAuthCallbackPermitLimit"] = "1000",
                        ["Access:AllowAllUsers"] = "true",
                        ["Security:TokenProtectionKey"] = "test-token-protection-key-with-enough-entropy"
                    };

                    foreach (var (key, value) in _configurationOverrides)
                    {
                        values[key] = value;
                    }

                    configuration.AddInMemoryCollection(values);
                });
            builder.ConfigureServices(
                services =>
                {
                    services.RemoveAll<DbContextOptions<CareerocDbContext>>();
                    // EF Core 9+ also registers provider configuration separately; drop it so only InMemory remains.
                    services.RemoveAll<IDbContextOptionsConfiguration<CareerocDbContext>>();
                    services.AddDbContext<CareerocDbContext>(
                        options => options.UseInMemoryDatabase(_databaseName));
                });
        }

        public async Task<ApiSeedData> SeedTwoUsersAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
            var now = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

            var studentUser = new User
            {
                Email = "student@example.com",
                Name = "Student User",
                TimeZoneId = ApplicationDefaults.DefaultTimeZoneId
            };
            var otherUser = new User
            {
                Email = "other@example.com",
                Name = "Other User",
                TimeZoneId = ApplicationDefaults.DefaultTimeZoneId
            };
            var studentInbox = new InboxAccount
            {
                User = studentUser,
                UserId = studentUser.Id,
                Provider = ApplicationDefaults.GmailInboxProvider,
                EmailAddress = "student.visible@gmail.com"
            };
            var otherInbox = new InboxAccount
            {
                User = otherUser,
                UserId = otherUser.Id,
                Provider = ApplicationDefaults.GmailInboxProvider,
                EmailAddress = "other@example.com"
            };
            var studentApplication = new JobApplication
            {
                User = studentUser,
                UserId = studentUser.Id,
                Company = "Student Visible Co",
                Role = "Student Visible Role",
                Status = ApplicationStatus.Interviewing,
                Priority = 4,
                CreatedAtUtc = now.AddDays(-3),
                LastActivityAtUtc = now.AddDays(-1)
            };
            var otherApplication = new JobApplication
            {
                User = otherUser,
                UserId = otherUser.Id,
                Company = "Other Hidden Co",
                Role = "Other Hidden Role",
                Status = ApplicationStatus.Offer,
                Priority = 5,
                CreatedAtUtc = now.AddDays(-2),
                LastActivityAtUtc = now
            };
            var studentEmail = new InboxEmail
            {
                InboxAccount = studentInbox,
                InboxAccountId = studentInbox.Id,
                JobApplication = studentApplication,
                JobApplicationId = studentApplication.Id,
                ExternalMessageId = "student-visible-email",
                ThreadId = "student-visible-thread",
                Sender = "recruiting@student-visible.example",
                Subject = "Student Visible Co interview",
                RawBody = "Student Visible Co interview details.",
                ParsedBody = "Student Visible Co interview details.",
                ReceivedAtUtc = now.AddDays(-1),
                Category = EmailCategory.InterviewInvitation,
                ExtractedCompany = "Student Visible Co",
                ExtractedRole = "Student Visible Role",
                RequiresManualReview = true,
                ReviewReason = "Visible review item for the signed user."
            };
            var otherEmail = new InboxEmail
            {
                InboxAccount = otherInbox,
                InboxAccountId = otherInbox.Id,
                JobApplication = otherApplication,
                JobApplicationId = otherApplication.Id,
                ExternalMessageId = "other-hidden-email",
                ThreadId = "other-hidden-thread",
                Sender = "recruiting@other-hidden.example",
                Subject = "Other Hidden Co offer",
                RawBody = "Other Hidden Co offer details.",
                ParsedBody = "Other Hidden Co offer details.",
                ReceivedAtUtc = now,
                Category = EmailCategory.Offer,
                ExtractedCompany = "Other Hidden Co",
                ExtractedRole = "Other Hidden Role",
                RequiresManualReview = true,
                ReviewReason = "Hidden review item for another user."
            };

            dbContext.Users.AddRange(studentUser, otherUser);
            dbContext.InboxAccounts.AddRange(studentInbox, otherInbox);
            dbContext.JobApplications.AddRange(studentApplication, otherApplication);
            dbContext.InboxEmails.AddRange(studentEmail, otherEmail);
            await dbContext.SaveChangesAsync();

            return new ApiSeedData(
                studentUser.Email,
                studentApplication.Id,
                otherApplication.Id);
        }
    }

    internal sealed record ApiSeedData(
        string StudentEmail,
        Guid StudentApplicationId,
        Guid OtherApplicationId);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Careeroc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
