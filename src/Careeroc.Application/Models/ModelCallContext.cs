using Careeroc.Application;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Models;

public sealed record ModelCallContext(
    string? UserEmail,
    Guid? InboxAccountId,
    Guid? InboxEmailId,
    string? RunId,
    string? CorrelationId = null)
{
    public static ModelCallContext FromInboxEmail(InboxEmail inboxEmail)
    {
        Guid? inboxAccountId = inboxEmail.InboxAccountId == Guid.Empty
            ? null
            : inboxEmail.InboxAccountId;
        Guid? inboxEmailId = inboxEmail.Id == Guid.Empty
            ? null
            : inboxEmail.Id;
        var userEmail = inboxEmail.InboxAccount?.User?.Email ??
                        inboxEmail.InboxAccount?.EmailAddress;

        return new ModelCallContext(
            string.IsNullOrWhiteSpace(userEmail) ? null : userEmail.Trim().ToLowerInvariant(),
            inboxAccountId,
            inboxEmailId,
            CareerocTelemetry.CurrentRunId ?? inboxEmailId?.ToString("N"),
            CareerocTelemetry.CurrentCorrelationId);
    }
}
