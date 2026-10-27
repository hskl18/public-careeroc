using Careeroc.Application.Abstractions;
using Careeroc.Domain.Entities;

namespace Careeroc.Infrastructure.Email;

public sealed class HeuristicDueDateExtractor : IExplicitDueDateExtractor
{
    private readonly HeuristicRecruitingEmailParser _heuristicParser;

    public HeuristicDueDateExtractor(HeuristicRecruitingEmailParser heuristicParser)
    {
        _heuristicParser = heuristicParser;
    }

    public Task<DateTimeOffset?> ExtractDueDateUtcAsync(
        InboxEmail inboxEmail,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_heuristicParser.Parse(inboxEmail).DueDateUtc);
    }
}
