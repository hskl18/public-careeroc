using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Abstractions;

public interface IRecruitingEmailParser
{
    Task<ParsedRecruitingEmail> ParseAsync(InboxEmail inboxEmail, CancellationToken cancellationToken = default);
}
