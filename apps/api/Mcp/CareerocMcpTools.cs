using System.ComponentModel;
using System.Text.Json.Serialization;
using Careeroc.Api.Endpoints;
using Careeroc.Application.Services;
using Careeroc.Contracts.Agents;
using Careeroc.Domain.Enums;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Careeroc.Api.Mcp;

// Read-only tools for a user's own assistant; the access token decides whose pipeline is visible.
[McpServerToolType]
public sealed class CareerocMcpTools
{
    private readonly AgentQueryService _queries;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CareerocMcpTools(AgentQueryService queries, IHttpContextAccessor httpContextAccessor)
    {
        _queries = queries;
        _httpContextAccessor = httpContextAccessor;
    }

    [McpServerTool(Name = "get_pipeline_summary", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Counts of job applications by status, the next open reminders and deadlines, and the most recently active applications.")]
    public Task<AgentPipelineSummaryDto> GetPipelineSummaryAsync(CancellationToken cancellationToken)
    {
        return _queries.GetPipelineSummaryAsync(CurrentUserEmail(), cancellationToken);
    }

    [McpServerTool(Name = "list_applications", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Job applications ordered by latest activity, optionally filtered by status.")]
    public Task<IReadOnlyList<AgentApplicationDto>> ListApplicationsAsync(
        [Description("Optional status filter.")] ApplicationStatusFilter? status = null,
        [Description("Maximum number of applications to return, 1 to 50.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return _queries.ListApplicationsAsync(
            CurrentUserEmail(),
            (ApplicationStatus?)status,
            limit,
            cancellationToken);
    }

    [McpServerTool(Name = "get_application", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("One application with its email-derived timeline and open reminders. Use an id from list_applications.")]
    public async Task<AgentApplicationDetailDto> GetApplicationAsync(
        [Description("Application id from list_applications.")] Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        return await _queries.GetApplicationAsync(CurrentUserEmail(), applicationId, cancellationToken)
            ?? throw new McpException($"No application {applicationId} exists in this workspace.");
    }

    private string CurrentUserEmail()
    {
        var request = _httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("MCP tools require an HTTP request context.");
        return RequestUserContext.GetUserEmail(request)
            ?? throw new InvalidOperationException("MCP tools require an authenticated user.");
    }
}

// The tool schema lists these labels, the same ones the tools return, so an assistant never guesses.
[JsonConverter(typeof(JsonStringEnumConverter<ApplicationStatusFilter>))]
public enum ApplicationStatusFilter
{
    [JsonStringEnumMemberName("applied")] Applied = ApplicationStatus.Applied,
    [JsonStringEnumMemberName("recruiter contact")] RecruiterContact = ApplicationStatus.RecruiterContact,
    [JsonStringEnumMemberName("assessment pending")] AssessmentPending = ApplicationStatus.OaPending,
    [JsonStringEnumMemberName("interview")] Interview = ApplicationStatus.Interviewing,
    [JsonStringEnumMemberName("offer")] Offer = ApplicationStatus.Offer,
    [JsonStringEnumMemberName("rejected")] Rejected = ApplicationStatus.Rejected,
    [JsonStringEnumMemberName("closed")] Closed = ApplicationStatus.Closed
}
