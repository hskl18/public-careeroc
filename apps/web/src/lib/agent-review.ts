import type { ProposedReminder, ResumeMailboxReview, UserMemoryEntry } from "./profile";

// A review waiting for approval is a pending memory entry; deciding on it goes through that entry.
export type AgentReview = ResumeMailboxReview & {
  pendingEntryId: string | null;
  pendingUpdatedAtUtc: string | null;
};

function isPending(entry: UserMemoryEntry) {
  return entry.confirmationStatus.toLowerCase() === "pending";
}

export function isAgentReviewEntry(entry: UserMemoryEntry) {
  return entry.kind === "orchestrator" && entry.key === "orchestrator.resume_mailbox_bridge";
}

function normalizeStringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string" && item.trim().length > 0)
    : [];
}

function normalizeAgentTrace(value: unknown): ResumeMailboxReview["trace"] {
  if (!value || typeof value !== "object") return null;
  const candidate = value as { steps?: unknown; generatedAtUtc?: unknown; summary?: unknown };
  if (!Array.isArray(candidate.steps)) return null;

  const steps = candidate.steps.flatMap((step) => {
    if (!step || typeof step !== "object") return [];
    const item = step as Record<string, unknown>;
    if (
      typeof item.stage !== "string" ||
      typeof item.agentName !== "string" ||
      typeof item.summary !== "string"
    ) {
      return [];
    }
    const facts = Array.isArray(item.facts)
      ? item.facts.flatMap((fact) => {
          if (!fact || typeof fact !== "object") return [];
          const factItem = fact as Record<string, unknown>;
          return typeof factItem.label === "string" && typeof factItem.value === "string"
            ? [{ label: factItem.label, value: factItem.value }]
            : [];
        })
      : [];
    return [
      {
        stage: item.stage,
        agentName: item.agentName,
        summary: item.summary,
        reason: typeof item.reason === "string" ? item.reason : null,
        facts,
      },
    ];
  });

  return {
    generatedAtUtc:
      typeof candidate.generatedAtUtc === "string" ? candidate.generatedAtUtc : null,
    steps,
    summary: null,
  };
}

function normalizeProposedReminders(value: unknown): ProposedReminder[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap((item) => {
    if (!item || typeof item !== "object") return [];
    const candidate = item as Record<string, unknown>;
    return typeof candidate.applicationId === "string" &&
      typeof candidate.company === "string" &&
      typeof candidate.role === "string" &&
      typeof candidate.title === "string" &&
      typeof candidate.dueInDays === "number"
      ? [
          {
            applicationId: candidate.applicationId,
            company: candidate.company,
            role: candidate.role,
            title: candidate.title,
            dueInDays: candidate.dueInDays,
          },
        ]
      : [];
  });
}

function parseStoredReview(entry: UserMemoryEntry): AgentReview | null {
  if (!isAgentReviewEntry(entry)) {
    return null;
  }

  try {
    const parsed = JSON.parse(entry.value) as Partial<ResumeMailboxReview>;
    const evidence = parsed.evidence;
    if (!parsed.summary || !evidence) return null;
    return {
      generatedAtUtc: parsed.generatedAtUtc ?? entry.updatedAtUtc,
      runtimePath: parsed.runtimePath ?? "career_orchestrator.resume_mailbox_review",
      modelPath: parsed.modelPath ?? "deterministic.shared_resume_mailbox_context.v1",
      summary: parsed.summary,
      findings: normalizeStringArray(parsed.findings),
      nextActions: normalizeStringArray(parsed.nextActions),
      evidence: {
        resumeVersions: Number(evidence.resumeVersions ?? 0),
        latestResumeScore: Number(evidence.latestResumeScore ?? 0),
        totalApplications: Number(evidence.totalApplications ?? 0),
        interviewOrOfferApplications: Number(evidence.interviewOrOfferApplications ?? 0),
        rejectedApplications: Number(evidence.rejectedApplications ?? 0),
        resumeVersionLinkedToOutcome: evidence.resumeVersionLinkedToOutcome ?? null,
      },
      threadId: parsed.threadId ?? null,
      status: parsed.status ?? "completed",
      requiresHumanReview: parsed.requiresHumanReview ?? false,
      confidence: parsed.confidence ?? null,
      trace: normalizeAgentTrace(parsed.trace),
      proposedReminders: normalizeProposedReminders(parsed.proposedReminders),
      remindersCreated:
        typeof parsed.remindersCreated === "number" ? parsed.remindersCreated : null,
      pendingEntryId: isPending(entry) ? entry.id : null,
      pendingUpdatedAtUtc: isPending(entry) ? entry.updatedAtUtc : null,
    };
  } catch {
    return null;
  }
}

export function findLatestAgentReview(entries: UserMemoryEntry[]): AgentReview | null {
  return entries
    .map(parseStoredReview)
    .filter((review): review is AgentReview => review !== null)
    .sort((a, b) => b.generatedAtUtc.localeCompare(a.generatedAtUtc))[0] ?? null;
}
