using Careeroc.Domain.Entities;

namespace Careeroc.Application.Services;

public static class InboxEmailLinkBuilder
{
    public static string? BuildGmailUrl(InboxEmail email)
    {
        return BuildGmailUrl(
            email.InboxAccount.Provider,
            email.InboxAccount.EmailAddress,
            email.ThreadId,
            email.ExternalMessageId);
    }

    public static string? BuildGmailUrl(
        string provider,
        string accountEmailAddress,
        string threadId,
        string externalMessageId)
    {
        if (!string.Equals(provider, ApplicationDefaults.GmailInboxProvider, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var targetId = string.IsNullOrWhiteSpace(threadId)
            ? externalMessageId
            : threadId;

        if (string.IsNullOrWhiteSpace(accountEmailAddress) ||
            string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        return $"https://mail.google.com/mail/?authuser={Uri.EscapeDataString(accountEmailAddress)}#all/{Uri.EscapeDataString(targetId)}";
    }
}
