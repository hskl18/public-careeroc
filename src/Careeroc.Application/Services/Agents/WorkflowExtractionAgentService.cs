using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Services.Agents;

public sealed class WorkflowExtractionAgentService
{
    private readonly IRecruitingEmailParser _recruitingEmailParser;

    public WorkflowExtractionAgentService(IRecruitingEmailParser recruitingEmailParser)
    {
        _recruitingEmailParser = recruitingEmailParser;
    }

    public Task<ParsedRecruitingEmail> ExtractAsync(
        InboxEmail inboxEmail,
        CancellationToken cancellationToken = default)
    {
        return _recruitingEmailParser.ParseAsync(inboxEmail, cancellationToken);
    }
}
