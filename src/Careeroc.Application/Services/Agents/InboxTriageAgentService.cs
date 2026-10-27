using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Services.Agents;

public sealed class InboxTriageAgentService
{
    public InboxTriageDecision Triage(InboxEmail inboxEmail)
    {
        var shortcut = InboxTriageShortcutRules.Match(inboxEmail);
        if (shortcut is not null)
        {
            return new InboxTriageDecision(
                false,
                shortcut.Reason,
                "Inbox Triage Shortcut",
                "Filtered routine non-career mail before workflow extraction.",
                shortcut.RuleId);
        }

        var isLikelyRecruiting = RecruitingEmailRelevance.IsLikelyRecruiting(
            inboxEmail.Sender,
            inboxEmail.Subject,
            inboxEmail.ParsedBody);

        return isLikelyRecruiting
            ? new InboxTriageDecision(
                true,
                "Matched recruiting-intent heuristics and should enter the workflow extraction path.",
                "Inbox Triage Agent",
                "Escalated to structured workflow extraction.")
            : new InboxTriageDecision(
                false,
                "Did not match recruiting-intent heuristics strongly enough to justify further parsing.",
                "Inbox Triage Agent",
                "Filtered inbox noise before workflow extraction.");
    }
}
