namespace Careeroc.Application.Models;

public sealed record InboxTriageDecision(
    bool IsRecruitingRelated,
    string RoutingReason,
    string AgentName,
    string VisibleOutput,
    string? ShortcutRuleId = null);
